using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Instances;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;

namespace AxoClient.Content
{
    /// <summary>
    /// Mods, Ressourcepacks und Shader einer Instanz über <see cref="ContentType"/>. Hält Ordner und
    /// <c>content.json</c> im Einklang und lädt Abhängigkeiten automatisch mit.
    /// </summary>
    public sealed class ContentManager : IContentManager
    {
        private readonly IInstanceManager _instances;
        private readonly IContentProviders _providers;
        private readonly IDownloadManager _downloads;
        private readonly IEventHub _events;
        private readonly IPathGuard _pathGuard;
        private readonly IAppLog _appLog;
        private readonly IContentFolders _contentFolders;
        private readonly IModMetadataReader _modMetadataReader;
        private readonly ILoaderNames _loaderNames;
        private readonly ITextHelper _textHelper;
        private readonly IVersionRange _versionRange;
        private readonly IModCompatibility _modCompatibility;
        private readonly Dictionary<string, SemaphoreSlim> _locks = new();

        public ContentManager(
            IInstanceManager instances,
            IContentProviders providers,
            IDownloadManager downloads,
            IEventHub events,
            IPathGuard pathGuard,
            IAppLog appLog,
            IContentFolders contentFolders,
            IModMetadataReader modMetadataReader,
            ILoaderNames loaderNames,
            ITextHelper textHelper,
            IVersionRange versionRange,
            IModCompatibility modCompatibility)
        {
            _instances = instances;
            _providers = providers;
            _downloads = downloads;
            _events = events;
            _pathGuard = pathGuard;
            _appLog = appLog;
            _contentFolders = contentFolders;
            _modMetadataReader = modMetadataReader;
            _loaderNames = loaderNames;
            _textHelper = textHelper;
            _versionRange = versionRange;
            _modCompatibility = modCompatibility;
        }

        private SemaphoreSlim lockFor(string id)
        {
            lock (_locks)
            {
                if (!_locks.TryGetValue(id, out var value))
                {
                    _locks[id] = value = new SemaphoreSlim(1, 1);
                }

                return value;
            }
        }

        public string Folder(string instanceId, ContentType type) =>
            Path.Combine(_instances.GameDirectory(instanceId), _contentFolders.Of(type));

        public async Task<IReadOnlyList<ContentItem>> List(string instanceId, ContentType type, CancellationToken cancellationToken = default)
        {
            var document = await Sync(instanceId, cancellationToken);
            var modLock = (await _instances.Get(instanceId, cancellationToken)).ModLock;
            return document.ListOf(type)
                .OrderBy(entry => entry.Source == ContentSource.AxoClient ? 0 : 1)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new ContentItem(entry.File, entry.Name, entry.Version, entry.Enabled, entry.Source, entry.ProjectId, entry.VersionId, entry.IconUrl,
                    entry.Author, entry.Size, entry.AddedAt, entry.Source == ContentSource.AxoClient, type == ContentType.Mod && modLock.IsLocked(entry),
                    entry.Previous != null ? entry.Previous.Version ?? entry.Previous.File : null))
                .ToList();
        }

        /// <summary>
        /// Gleicht Ordner und content.json ab: unbekannte Dateien werden als <c>local</c> eingetragen, Einträge ohne
        /// Datei entfernt. Hashes werden nur neu berechnet, wenn sich Größe oder Änderungszeit geändert haben.
        /// </summary>
        public async Task<ContentDocument> Sync(string instanceId, CancellationToken cancellationToken = default)
        {
            var gate = lockFor(instanceId);
            await gate.WaitAsync(cancellationToken);
            try
            {
                var store = _instances.ContentOf(instanceId);
                var document = await store.Load(cancellationToken);
                var changed = false;
                foreach (var type in Enum.GetValues<ContentType>())
                {
                    changed |= syncType(instanceId, type, document);
                }

                if (changed)
                {
                    document.ChangedAt = DateTimeOffset.UtcNow;
                    await store.Save(document, cancellationToken);
                }
                return document;
            }
            finally
            {
                gate.Release();
            }
        }

        private bool syncType(string instanceId, ContentType type, ContentDocument document)
        {
            var directory = Folder(instanceId, type);
            Directory.CreateDirectory(directory);
            var list = document.ListOf(type);
            var changed = false;

            var onDisk = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in Directory.EnumerateFiles(directory).Select(file => new FileInfo(file)).Where(file => IsContentFile(type, file.Name))
                         .GroupBy(file => BaseName(file.Name), StringComparer.OrdinalIgnoreCase))
            {
                onDisk[group.Key] = group.Count() == 1 ? group.First() : resolveDouble(directory, group.Key, group.ToList(), list);
            }

            var onDiskDirectories = type != ContentType.Mod
                ? Directory.EnumerateDirectories(directory).Select(folderPath => new DirectoryInfo(folderPath)).ToDictionary(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                : [];

            foreach (var entry in list.ToList())
            {
                if (onDisk.TryGetValue(entry.File, out var file))
                {
                    var enabled = !file.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    if (entry.Enabled != enabled)
                    {
                        entry.Enabled = enabled;
                        changed = true;
                    }
                    // Metadaten eines älteren Lesers (ohne Versionsbedingungen) einmal neu lesen
                    if (entry.Size != file.Length || entry.Modified != file.LastWriteTimeUtc
                        || type == ContentType.Mod && (entry.Meta?.Format ?? entry.MetaFormat) < ModMetadata.CurrentFormat)
                    {
                        refreshFileInfo(entry, file, type);
                        changed = true;
                    }
                    onDisk.Remove(entry.File);
                }
                else if (onDiskDirectories.ContainsKey(entry.File))
                {
                    onDiskDirectories.Remove(entry.File);
                }
                else
                {
                    list.Remove(entry);
                    changed = true;
                }
            }

            foreach (var (name, file) in onDisk)
            {
                var entry = new ContentEntry
                {
                    File = name,
                    Name = Path.GetFileNameWithoutExtension(name),
                    Enabled = !file.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
                    Source = ContentSource.Local,
                    AddedAt = file.CreationTimeUtc
                };
                refreshFileInfo(entry, file, type);
                list.Add(entry);
                changed = true;
            }
            // Entpackte Ressourcepacks und Shader als Ordner
            foreach (var (name, folderInfo) in onDiskDirectories)
            {
                list.Add(new ContentEntry { File = name, Name = name, Source = ContentSource.Local, AddedAt = folderInfo.CreationTimeUtc });
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// "x.jar" und "x.jar.disabled" liegen beide im Ordner (z. B. nach einem Pack-Update): Minecraft lädt dann die aktive
        /// trotz Deaktivierung. Die neuere Datei bleibt im Zustand, den content.json kennt (sonst aktiv), die andere fällt weg.
        /// </summary>
        private FileInfo resolveDouble(string directory, string baseName, List<FileInfo> files, List<ContentEntry> list)
        {
            var known = list.FirstOrDefault(entry => entry.File.Equals(baseName, StringComparison.OrdinalIgnoreCase));
            var target = Path.Combine(directory, known is { Enabled: false } ? baseName + ".disabled" : baseName);
            var newest = files.OrderByDescending(file => file.LastWriteTimeUtc).First();
            try
            {
                if (!newest.FullName.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(newest.FullName, target, overwrite: true);
                }

                foreach (var other in files.Where(file => !file.FullName.Equals(target, StringComparison.OrdinalIgnoreCase)
                                                       && !file.FullName.Equals(newest.FullName, StringComparison.OrdinalIgnoreCase)))
                {
                    File.Delete(other.FullName);
                }

                return new FileInfo(target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _appLog.Warn($"Doppelte Datei {baseName} konnte nicht bereinigt werden", exception);
                return files.First(file => file.FullName.Equals(target, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void refreshFileInfo(ContentEntry entry, FileInfo file, ContentType type)
        {
            entry.Size = file.Length;
            entry.Modified = file.LastWriteTimeUtc;
            using (var stream = file.OpenRead())
            {
                entry.Hashes = new ContentHashes(_textHelper.Sha1Hex(stream), null);
            }

            if (type != ContentType.Mod)
            {
                return;
            }

            var meta = _modMetadataReader.Read(file.FullName);
            entry.Meta = meta?.Meta;
            entry.MetaFormat = ModMetadata.CurrentFormat;
            if (meta != null && entry.Source == ContentSource.Local)
            {
                if (!string.IsNullOrWhiteSpace(meta.Name))
                {
                    entry.Name = _textHelper.Clean(meta.Name, 100);
                }

                entry.Version ??= _textHelper.Clean(meta.Version, 60) is { Length: > 0 } cleanVersion ? cleanVersion : null;
                entry.Author ??= _textHelper.Clean(meta.Author, 60) is { Length: > 0 } cleanAuthor ? cleanAuthor : null;
            }
        }

        public bool IsContentFile(ContentType type, string name)
        {
            var baseName = BaseName(name);
            var ext = Path.GetExtension(baseName).ToLowerInvariant();
            return type == ContentType.Mod ? ext == ".jar" : ext == ".zip";
        }

        public string BaseName(string name) =>
            name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? name[..^".disabled".Length] : name;

        /// <summary>Unbekannte lokale Dateien bei Modrinth erkennen, damit sie Name, Icon und Updates bekommen.</summary>
        public async Task IdentifyLocal(string instanceId, CancellationToken cancellationToken = default)
        {
            var document = await Sync(instanceId, cancellationToken);
            var unknown = Enum.GetValues<ContentType>().SelectMany(document.ListOf)
                .Where(entry => entry.Source == ContentSource.Local && entry.Hashes.Sha1 != null && entry.ProjectId == null).ToList();
            if (unknown.Count == 0)
            {
                return;
            }

            var modrinth = _providers.Get("modrinth");
            var found = await modrinth.Identify(unknown.Select(entry => new LocalFileHash(entry.Hashes.Sha1!, null)).ToList(), cancellationToken);
            if (found.Count == 0)
            {
                return;
            }

            var projects = await modrinth.GetProjects(found.Values.Select(value => value.ProjectId), cancellationToken);
            await Change(instanceId, content =>
            {
                foreach (var entry in Enum.GetValues<ContentType>().SelectMany(content.ListOf))
                {
                    if (entry.Source != ContentSource.Local || entry.Hashes.Sha1 is not { } sha1 || !found.TryGetValue(sha1, out var version))
                    {
                        continue;
                    }

                    entry.Source = ContentSource.Modrinth;
                    entry.ProjectId = version.ProjectId;
                    entry.VersionId = version.Id;
                    entry.Version = version.VersionNumber;
                    if (projects.TryGetValue(version.ProjectId, out var project))
                    {
                        entry.Name = project.Title;
                        entry.IconUrl = project.IconUrl;
                    }
                    entry.Dependencies = version.Dependencies.Where(dependency => dependency.Kind == DependencyKind.Required && dependency.ProjectId != null)
                        .Select(dependency => dependency.ProjectId!).ToList();
                }
            }, cancellationToken);
        }

        /// <summary>Installiert eine Version samt Pflicht-Abhängigkeiten. Gibt die installierten Dateinamen zurück.</summary>
        public async Task<IReadOnlyList<string>> Install(string instanceId, ContentType type, InstallRequest request,
            JobContext job, bool ignoreModLock = false)
        {
            var instance = await _instances.Get(instanceId, job.Token);
            if (type == ContentType.Mod && !ignoreModLock)
            {
                instance.ModLock.ThrowIfCannotAdd();
                // Pack-Mods nicht durch eine andere Version ersetzen
                var existing = (await Sync(instanceId, job.Token)).Mods.FirstOrDefault(mod => mod.ProjectId == request.ProjectId);
                if (existing != null)
                {
                    instance.ModLock.ThrowIfLocked(existing);
                }
            }
            var provider = _providers.Get(request.Source);
            var kind = ToKind(type);
            var minecraftVersion = instance.Minecraft.Version;
            var loader = instance.Loader.Type;

            var document = await Sync(instanceId, job.Token);
            var installedProjects = Enum.GetValues<ContentType>().SelectMany(document.ListOf)
                .Where(entry => entry.ProjectId != null).Select(entry => entry.ProjectId!).ToHashSet();

            // Abhängigkeiten auflösen (Breitensuche, höchstens 40 Projekte)
            var plan = new List<(ContentVersion Version, ContentType Type, bool IsDependency)>();
            var queue = new Queue<(string ProjectId, string? VersionId, ContentType Type, bool Dep)>();
            queue.Enqueue((request.ProjectId, request.VersionId, type, false));
            var seen = new HashSet<string>();
            while (queue.Count > 0 && plan.Count < 40)
            {
                var (projectId, versionId, itemType, isDependency) = queue.Dequeue();
                if (!seen.Add(projectId) || (isDependency && installedProjects.Contains(projectId)))
                {
                    continue;
                }

                job.Status("content.resolving");
                var version = versionId != null
                    ? await provider.GetVersion(versionId, job.Token)
                    : (await provider.GetVersions(projectId, minecraftVersion, loader, ToKind(itemType), job.Token)).FirstOrDefault(candidateVersion => candidateVersion.Channel == "release")
                      ?? (await provider.GetVersions(projectId, minecraftVersion, loader, ToKind(itemType), job.Token)).FirstOrDefault();
                if (version == null)
                {
                    if (isDependency)
                    {
                        continue;
                    }

                    throw new AxoException("content.noCompatibleVersion", new() { ["version"] = minecraftVersion });
                }
                plan.Add((version, itemType, isDependency));
                if (itemType == ContentType.Mod)
                {
                    foreach (var dependency in version.Dependencies.Where(dependency => dependency.Kind == DependencyKind.Required && dependency.ProjectId != null))
                    {
                        queue.Enqueue((dependency.ProjectId!, dependency.VersionId, ContentType.Mod, true));
                    }
                }
            }

            var projects = await provider.GetProjects(plan.Select(plannedItem => plannedItem.Version.ProjectId), job.Token);
            var requests = new List<DownloadRequest>();
            var entries = new List<(ContentEntry Entry, ContentType Type)>();
            foreach (var (version, itemType, _) in plan)
            {
                var file = version.PrimaryFile ?? throw new AxoException("content.noFile");
                if (string.IsNullOrEmpty(file.Url))
                {
                    throw new AxoException("content.manualDownload", new() { ["name"] = projects.GetValueOrDefault(version.ProjectId)?.Title ?? file.FileName, ["projectId"] = version.ProjectId });
                }

                if (!_pathGuard.IsSafeFileName(file.FileName) || !IsContentFile(itemType, file.FileName))
                {
                    throw new AxoException("errors.invalidFileName");
                }

                var target = _pathGuard.Combine(Folder(instanceId, itemType), file.FileName);
                requests.Add(new DownloadRequest(file.Url, target, file.Sha1, file.Sha512, file.Size > 0 ? file.Size : null));
                var project = projects.GetValueOrDefault(version.ProjectId);
                entries.Add((new ContentEntry
                {
                    File = file.FileName,
                    Name = project?.Title ?? version.Name,
                    Version = version.VersionNumber,
                    Source = request.Source == "curseforge" ? ContentSource.CurseForge : ContentSource.Modrinth,
                    ProjectId = version.ProjectId,
                    VersionId = version.Id,
                    Hashes = new ContentHashes(file.Sha1, file.Sha512),
                    Size = file.Size,
                    Dependencies = version.Dependencies.Where(dependency => dependency.Kind == DependencyKind.Required && dependency.ProjectId != null)
                        .Select(dependency => dependency.ProjectId!).ToList(),
                    IconUrl = project?.IconUrl,
                    Author = project?.Author is { Length: > 0 } author ? author : null
                }, itemType));
            }

            job.Status("content.downloading");
            await _downloads.DownloadAll(requests, job.Range(0.05, 0.95), job.Token);

            await Change(instanceId, content =>
            {
                foreach (var (entry, itemType) in entries)
                {
                    var list = content.ListOf(itemType);
                    // Alte Datei desselben Projekts ersetzen (Update/Version ändern). Sie wandert nach previous/, damit sich
                    // das Update rückgängig machen lässt.
                    foreach (var old in list.Where(existing => existing.ProjectId == entry.ProjectId && existing.File != entry.File).ToList())
                    {
                        entry.Previous = keepAsPrevious(instanceId, itemType, old) ? old : null;
                        if (entry.Previous == null)
                        {
                            tryDelete(Path.Combine(Folder(instanceId, itemType), old.DiskName));
                        }

                        list.Remove(old);
                    }
                    list.RemoveAll(existing => existing.File.Equals(entry.File, StringComparison.OrdinalIgnoreCase));
                    var path = Path.Combine(Folder(instanceId, itemType), entry.File);
                    var file = new FileInfo(path);
                    entry.Modified = file.LastWriteTimeUtc;
                    entry.Size = file.Length;
                    if (itemType == ContentType.Mod)
                    {
                        entry.Meta = _modMetadataReader.Read(path)?.Meta;
                    }

                    list.Add(entry);
                }
            }, job.Token);
            return entries.Select(entry => entry.Entry.File).ToList();
        }

        /// <summary>Lokale Datei hinzufügen (Drag &amp; Drop). Gilt als "Lokal · eigene Mod".</summary>
        public async Task<string> AddLocalFile(string instanceId, ContentType type, string sourcePath, CancellationToken cancellationToken = default)
        {
            var name = Path.GetFileName(sourcePath);
            if (!_pathGuard.IsSafeFileName(name) || !IsContentFile(type, name))
            {
                throw new AxoException("content.wrongFileType");
            }

            if (type == ContentType.Mod)
            {
                var modLock = (await _instances.Get(instanceId, cancellationToken)).ModLock;
                modLock.ThrowIfCannotAdd();
                if ((await Sync(instanceId, cancellationToken)).Mods.FirstOrDefault(mod => mod.File.Equals(BaseName(name), StringComparison.OrdinalIgnoreCase)) is { } existing)
                {
                    modLock.ThrowIfLocked(existing);
                }
            }
            var target = _pathGuard.Combine(Folder(instanceId, type), name);
            await using (var source = File.OpenRead(sourcePath))
            await using (var dest = File.Create(target))
            {
                await source.CopyToAsync(dest, cancellationToken);
            }

            await Sync(instanceId, cancellationToken);
            return name;
        }

        public async Task SetEnabled(string instanceId, ContentType type, string file, bool enabled, CancellationToken cancellationToken = default)
        {
            var modLock = (await _instances.Get(instanceId, cancellationToken)).ModLock;
            await Change(instanceId, document =>
            {
                var entry = find(document, type, file);
                if (entry.Source == ContentSource.AxoClient)
                {
                    throw new AxoException("content.locked");
                }

                if (type == ContentType.Mod)
                {
                    modLock.ThrowIfLocked(entry);
                }
                // Nach dem tatsächlichen Stand auf der Platte richten, nicht nur nach content.json: sonst passiert nichts,
                // wenn beide auseinanderlaufen, oder es liegen "x.jar" und "x.jar.disabled" nebeneinander und die aktive lädt weiter.
                var directory = Folder(instanceId, type);
                var on = Path.Combine(directory, entry.File);
                var off = on + ".disabled";
                var to = enabled ? on : off;
                var from = File.Exists(enabled ? off : on) ? (enabled ? off : on) : null;
                if (from != null)
                {
                    try
                    {
                        File.Move(from, to, overwrite: true);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Meist hält das laufende Spiel die Datei fest
                        throw new AxoException("content.fileInUse", new() { ["name"] = entry.Name }, exception);
                    }
                }
                entry.Enabled = enabled;
            }, cancellationToken);
        }

        public async Task Remove(string instanceId, ContentType type, IReadOnlyCollection<string> files, CancellationToken cancellationToken = default)
        {
            var modLock = (await _instances.Get(instanceId, cancellationToken)).ModLock;
            await Change(instanceId, document =>
            {
                var list = document.ListOf(type);
                // Erst alles prüfen, dann löschen: bei gemischter Auswahl nichts halb erledigen
                foreach (var file in files)
                {
                    var entry = find(document, type, file);
                    if (entry.Source == ContentSource.AxoClient)
                    {
                        throw new AxoException("content.locked");
                    }

                    if (type == ContentType.Mod)
                    {
                        modLock.ThrowIfLocked(entry);
                    }
                }
                foreach (var file in files)
                {
                    var entry = find(document, type, file);
                    var path = Path.Combine(Folder(instanceId, type), entry.DiskName);
                    if (File.Exists(path))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    else if (Directory.Exists(Path.Combine(Folder(instanceId, type), entry.File)))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(Path.Combine(Folder(instanceId, type), entry.File),
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }

                    if (entry.Previous != null)
                    {
                        tryDelete(Path.Combine(previousDirectory(instanceId, type), entry.Previous.File));
                    }

                    list.Remove(entry);
                }
            }, cancellationToken);
        }

        /// <summary>Mod-Schutz umstellen. <paramref name="adoptCurrent"/>: alle jetzigen Mods zählen ab sofort zum Pack.</summary>
        public async Task<ModLock> SetModLock(string instanceId, ModLockLevel level, bool adoptCurrent, CancellationToken cancellationToken = default)
        {
            var document = await Sync(instanceId, cancellationToken);
            var instance = await _instances.Get(instanceId, cancellationToken);
            instance.ModLock.SetLevel(level, document.Mods, adoptCurrent);
            await _instances.Save(instance, cancellationToken);
            _events.Publish("content.changed", new { instanceId });
            return instance.ModLock;
        }

        /// <summary>Verfügbare Versionen für "Version ändern".</summary>
        public async Task<IReadOnlyList<ContentVersion>> VersionsFor(string instanceId, ContentType type, string file,
            bool allMinecraftVersions, CancellationToken cancellationToken = default)
        {
            var instance = await _instances.Get(instanceId, cancellationToken);
            var document = await Sync(instanceId, cancellationToken);
            var entry = find(document, type, file);
            if (entry.ProjectId == null || entry.Source is not (ContentSource.Modrinth or ContentSource.CurseForge))
            {
                throw new AxoException("content.noSource");
            }

            return await _providers.Get(entry.Source).GetVersions(entry.ProjectId,
                allMinecraftVersions ? null : instance.Minecraft.Version, instance.Loader.Type, ToKind(type), cancellationToken);
        }

        /// <summary>
        /// Nach einem Wechsel von Minecraft-Version oder Loader: Mods von Modrinth/CurseForge auf eine passende Version
        /// umstellen, Mods ohne passende Version deaktivieren (nicht löschen). Lokale Mods werden nur deaktiviert, wenn ihre
        /// Metadaten klar sagen, dass sie nicht passen. Gesperrte Pack-Mods und bereits deaktivierte Mods bleiben, wie sie sind.
        /// </summary>
        public async Task<ModAdaptResult> AdaptMods(string instanceId, JobContext job)
        {
            var instance = await _instances.Get(instanceId, job.Token);
            var minecraftVersion = instance.Minecraft.Version;
            var loader = instance.Loader.Type;
            var result = new ModAdaptResult();
            if (loader == LoaderType.Vanilla)
            {
                return result;
            }

            var mods = (await Sync(instanceId, job.Token)).Mods
                .Where(mod => mod.Enabled && mod.Source != ContentSource.AxoClient && !instance.ModLock.IsLocked(mod)).ToList();
            for (var index = 0; index < mods.Count; index++)
            {
                job.Token.ThrowIfCancellationRequested();
                var mod = mods[index];
                job.Status("content.adapt.checking", ("index", index + 1), ("count", mods.Count));
                job.Report((double)index / Math.Max(1, mods.Count));
                try
                {
                    if (mod.ProjectId != null && mod.Source is ContentSource.Modrinth or ContentSource.CurseForge)
                    {
                        var source = mod.Source == ContentSource.CurseForge ? "curseforge" : "modrinth";
                        var versions = await _providers.Get(source).GetVersions(mod.ProjectId, minecraftVersion, loader, ProjectKind.Mod, job.Token);
                        if (versions.Any(version => version.Id == mod.VersionId))
                        {
                            continue; // Die installierte Version passt schon
                        }

                        var best = versions.FirstOrDefault(version => version.Channel == "release") ?? versions.FirstOrDefault();
                        if (best == null)
                        {
                            await SetEnabled(instanceId, ContentType.Mod, mod.File, false, job.Token);
                            result.Disabled.Add(mod.Name);
                            continue;
                        }
                        await Install(instanceId, ContentType.Mod, new InstallRequest(source, mod.ProjectId, best.Id),
                            job.Sub((double)index / mods.Count, (double)(index + 1) / mods.Count));
                        result.Updated++;
                    }
                    else if (mod.Meta is { } meta && !_modCompatibility.ModFits(meta, loader, minecraftVersion))
                    {
                        await SetEnabled(instanceId, ContentType.Mod, mod.File, false, job.Token);
                        result.Disabled.Add(mod.Name);
                    }
                }
                catch (Exception exception) when (exception is AxoException or HttpRequestException)
                {
                    // Offline, Datei gerade in Benutzung o. Ä.: Mod bleibt unverändert, die Startprüfung meldet sie ggf.
                    _appLog.Warn($"Mod nicht angepasst: {mod.Name}", exception);
                    result.Failed++;
                }
            }
            job.Report(1);
            return result;
        }

        public async Task Change(string instanceId, Action<ContentDocument> change, CancellationToken cancellationToken = default)
        {
            var gate = lockFor(instanceId);
            await gate.WaitAsync(cancellationToken);
            try
            {
                await _instances.ContentOf(instanceId).Update(document =>
                {
                    change(document);
                    document.ChangedAt = DateTimeOffset.UtcNow;
                }, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
            _events.Publish("content.changed", new { instanceId });
        }

        private ContentEntry find(ContentDocument document, ContentType type, string file) =>
            document.ListOf(type).FirstOrDefault(entry => entry.File.Equals(file, StringComparison.OrdinalIgnoreCase))
            ?? throw new AxoException("errors.notFound");

        public ProjectKind ToKind(ContentType type) => type switch
        {
            ContentType.Mod => ProjectKind.Mod,
            ContentType.ResourcePack => ProjectKind.ResourcePack,
            _ => ProjectKind.ShaderPack
        };

        private void tryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException exception)
            {
                _appLog.Warn($"Alte Datei konnte nicht gelöscht werden: {Path.GetFileName(path)}", exception);
            }
        }

        public bool SupportsMods(Instance instance) => _loaderNames.SupportsMods(instance.Loader.Type);

        private string previousDirectory(string instanceId, ContentType type) =>
            Path.Combine(Path.GetDirectoryName(_instances.GameDirectory(instanceId))!, "previous", _contentFolders.Of(type));

        /// <summary>Alte Datei nach <c>previous/</c> verschieben (eine Stufe: die davor verschwindet). False, wenn das nicht ging.</summary>
        private bool keepAsPrevious(string instanceId, ContentType type, ContentEntry old)
        {
            var from = Path.Combine(Folder(instanceId, type), old.DiskName);
            if (!File.Exists(from))
            {
                return false;
            }

            try
            {
                var directory = previousDirectory(instanceId, type);
                Directory.CreateDirectory(directory);
                if (old.Previous != null)
                {
                    tryDelete(Path.Combine(directory, old.Previous.File));
                }

                File.Move(from, Path.Combine(directory, old.File), overwrite: true);
                old.Previous = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _appLog.Warn($"Vorige Version nicht aufbewahrt: {old.File}", exception);
                return false;
            }
        }

        /// <summary>Letztes Update bzw. letzten Versionswechsel eines Inhalts zurücknehmen.</summary>
        public async Task Rollback(string instanceId, ContentType type, string file, CancellationToken cancellationToken = default)
        {
            var modLock = (await _instances.Get(instanceId, cancellationToken)).ModLock;
            await Change(instanceId, document =>
            {
                var entry = find(document, type, file);
                if (type == ContentType.Mod)
                {
                    modLock.ThrowIfLocked(entry);
                }

                var previous = entry.Previous ?? throw new AxoException("content.noPrevious");
                var stored = Path.Combine(previousDirectory(instanceId, type), previous.File);
                if (!File.Exists(stored))
                {
                    entry.Previous = null;
                    throw new AxoException("content.noPrevious");
                }
                var directory = Folder(instanceId, type);
                var target = Path.Combine(directory, previous.DiskName);
                try
                {
                    File.Move(stored, target, overwrite: true);
                    if (!previous.File.Equals(entry.File, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(Path.Combine(directory, entry.File));
                        File.Delete(Path.Combine(directory, entry.File + ".disabled"));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new AxoException("content.fileInUse", new() { ["name"] = entry.Name }, exception);
                }
                var fileInfo = new FileInfo(target);
                previous.Modified = fileInfo.LastWriteTimeUtc;
                previous.Size = fileInfo.Length;
                previous.Previous = null;
                var list = document.ListOf(type);
                list[list.IndexOf(entry)] = previous;
                // Mit der alten Version neu prüfen lassen
                document.CheckedAt = null;
            }, cancellationToken);
        }
    }
}
