using AxoClient.Core.Common;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Content;

/// <summary>Ein Inhalt, wie ihn die UI-Liste anzeigt.</summary>
public sealed record ContentItem(
    string File,
    string Name,
    string? Version,
    bool Enabled,
    ContentSource Source,
    string? ProjectId,
    string? VersionId,
    string? IconUrl,
    string? Author,
    long Size,
    DateTimeOffset AddedAt,
    bool Locked,
    bool ModLocked);

/// <summary>Was installiert werden soll: eine Version aus einer Quelle.</summary>
public sealed record InstallRequest(string Source, string ProjectId, string? VersionId = null);

/// <summary>
/// Mods, Ressourcepacks und Shader einer Instanz über <see cref="ContentType"/>. Hält Ordner und
/// <c>content.json</c> im Einklang und lädt Abhängigkeiten automatisch mit.
/// </summary>
public sealed class ContentManager(
    InstanceManager instances,
    ContentProviders providers,
    DownloadManager downloads,
    EventHub events)
{
    private readonly Dictionary<string, SemaphoreSlim> _locks = new();

    private SemaphoreSlim LockFor(string id)
    {
        lock (_locks)
        {
            if (!_locks.TryGetValue(id, out var l))
                _locks[id] = l = new SemaphoreSlim(1, 1);
            return l;
        }
    }

    public string Folder(string instanceId, ContentType type) =>
        Path.Combine(instances.GameDir(instanceId), ContentFolders.Of(type));

    public async Task<IReadOnlyList<ContentItem>> ListAsync(string instanceId, ContentType type, CancellationToken ct = default)
    {
        var doc = await SyncAsync(instanceId, ct);
        var modLock = (await instances.GetAsync(instanceId, ct)).ModLock;
        return doc.ListOf(type)
            .OrderBy(e => e.Source == ContentSource.AxoClient ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => new ContentItem(e.File, e.Name, e.Version, e.Enabled, e.Source, e.ProjectId, e.VersionId, e.IconUrl,
                e.Author, e.Size, e.AddedAt, e.Source == ContentSource.AxoClient, type == ContentType.Mod && modLock.IsLocked(e)))
            .ToList();
    }

    /// <summary>
    /// Gleicht Ordner und content.json ab: unbekannte Dateien werden als <c>local</c> eingetragen, Einträge ohne
    /// Datei entfernt. Hashes werden nur neu berechnet, wenn sich Größe oder Änderungszeit geändert haben.
    /// </summary>
    public async Task<ContentDocument> SyncAsync(string instanceId, CancellationToken ct = default)
    {
        var gate = LockFor(instanceId);
        await gate.WaitAsync(ct);
        try
        {
            var store = instances.ContentOf(instanceId);
            var doc = await store.LoadAsync(ct);
            var changed = false;
            foreach (var type in Enum.GetValues<ContentType>())
                changed |= SyncType(instanceId, type, doc);
            if (changed)
            {
                doc.ChangedAt = DateTimeOffset.UtcNow;
                await store.SaveAsync(doc, ct);
            }
            return doc;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool SyncType(string instanceId, ContentType type, ContentDocument doc)
    {
        var dir = Folder(instanceId, type);
        Directory.CreateDirectory(dir);
        var list = doc.ListOf(type);
        var changed = false;

        var onDisk = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Directory.EnumerateFiles(dir).Select(f => new FileInfo(f)).Where(f => IsContentFile(type, f.Name))
                     .GroupBy(f => BaseName(f.Name), StringComparer.OrdinalIgnoreCase))
            onDisk[group.Key] = group.Count() == 1 ? group.First() : ResolveDouble(dir, group.Key, group.ToList(), list);
        var onDiskDirs = type != ContentType.Mod
            ? Directory.EnumerateDirectories(dir).Select(d => new DirectoryInfo(d)).ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase)
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
                    RefreshFileInfo(entry, file, type);
                    changed = true;
                }
                onDisk.Remove(entry.File);
            }
            else if (onDiskDirs.ContainsKey(entry.File))
            {
                onDiskDirs.Remove(entry.File);
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
            RefreshFileInfo(entry, file, type);
            list.Add(entry);
            changed = true;
        }
        // Entpackte Ressourcepacks und Shader als Ordner
        foreach (var (name, info) in onDiskDirs)
        {
            list.Add(new ContentEntry { File = name, Name = name, Source = ContentSource.Local, AddedAt = info.CreationTimeUtc });
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// "x.jar" und "x.jar.disabled" liegen beide im Ordner (z. B. nach einem Pack-Update): Minecraft lädt dann die aktive
    /// trotz Deaktivierung. Die neuere Datei bleibt im Zustand, den content.json kennt (sonst aktiv), die andere fällt weg.
    /// </summary>
    private static FileInfo ResolveDouble(string dir, string baseName, List<FileInfo> files, List<ContentEntry> list)
    {
        var known = list.FirstOrDefault(e => e.File.Equals(baseName, StringComparison.OrdinalIgnoreCase));
        var target = Path.Combine(dir, known is { Enabled: false } ? baseName + ".disabled" : baseName);
        var newest = files.OrderByDescending(f => f.LastWriteTimeUtc).First();
        try
        {
            if (!newest.FullName.Equals(target, StringComparison.OrdinalIgnoreCase))
                File.Move(newest.FullName, target, overwrite: true);
            foreach (var other in files.Where(f => !f.FullName.Equals(target, StringComparison.OrdinalIgnoreCase)
                                                   && !f.FullName.Equals(newest.FullName, StringComparison.OrdinalIgnoreCase)))
                File.Delete(other.FullName);
            return new FileInfo(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Doppelte Datei {baseName} konnte nicht bereinigt werden", ex);
            return files.First(f => f.FullName.Equals(target, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void RefreshFileInfo(ContentEntry entry, FileInfo file, ContentType type)
    {
        entry.Size = file.Length;
        entry.Modified = file.LastWriteTimeUtc;
        using (var stream = file.OpenRead())
            entry.Hashes = new ContentHashes(Text.Sha1Hex(stream), null);
        if (type != ContentType.Mod)
            return;
        var meta = ModMetadataReader.Read(file.FullName);
        entry.Meta = meta?.Meta;
        entry.MetaFormat = ModMetadata.CurrentFormat;
        if (meta != null && entry.Source == ContentSource.Local)
        {
            if (!string.IsNullOrWhiteSpace(meta.Name))
                entry.Name = Text.Clean(meta.Name, 100);
            entry.Version ??= Text.Clean(meta.Version, 60) is { Length: > 0 } v ? v : null;
            entry.Author ??= Text.Clean(meta.Author, 60) is { Length: > 0 } a ? a : null;
        }
    }

    internal static bool IsContentFile(ContentType type, string name)
    {
        var baseName = BaseName(name);
        var ext = Path.GetExtension(baseName).ToLowerInvariant();
        return type == ContentType.Mod ? ext == ".jar" : ext == ".zip";
    }

    internal static string BaseName(string name) =>
        name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? name[..^".disabled".Length] : name;

    /// <summary>Unbekannte lokale Dateien bei Modrinth erkennen, damit sie Name, Icon und Updates bekommen.</summary>
    public async Task IdentifyLocalAsync(string instanceId, CancellationToken ct = default)
    {
        var doc = await SyncAsync(instanceId, ct);
        var unknown = Enum.GetValues<ContentType>().SelectMany(doc.ListOf)
            .Where(e => e.Source == ContentSource.Local && e.Hashes.Sha1 != null && e.ProjectId == null).ToList();
        if (unknown.Count == 0)
            return;
        var modrinth = providers.Get("modrinth");
        var found = await modrinth.IdentifyAsync(unknown.Select(e => new LocalFileHash(e.Hashes.Sha1!, null)).ToList(), ct);
        if (found.Count == 0)
            return;
        var projects = await modrinth.GetProjectsAsync(found.Values.Select(v => v.ProjectId), ct);
        await ChangeAsync(instanceId, d =>
        {
            foreach (var entry in Enum.GetValues<ContentType>().SelectMany(d.ListOf))
            {
                if (entry.Source != ContentSource.Local || entry.Hashes.Sha1 is not { } sha1 || !found.TryGetValue(sha1, out var version))
                    continue;
                entry.Source = ContentSource.Modrinth;
                entry.ProjectId = version.ProjectId;
                entry.VersionId = version.Id;
                entry.Version = version.VersionNumber;
                if (projects.TryGetValue(version.ProjectId, out var project))
                {
                    entry.Name = project.Title;
                    entry.IconUrl = project.IconUrl;
                }
                entry.Dependencies = version.Dependencies.Where(x => x.Kind == DependencyKind.Required && x.ProjectId != null)
                    .Select(x => x.ProjectId!).ToList();
            }
        }, ct);
    }

    /// <summary>Installiert eine Version samt Pflicht-Abhängigkeiten. Gibt die installierten Dateinamen zurück.</summary>
    public async Task<IReadOnlyList<string>> InstallAsync(string instanceId, ContentType type, InstallRequest request,
        JobContext job, bool ignoreModLock = false)
    {
        var instance = await instances.GetAsync(instanceId, job.Token);
        if (type == ContentType.Mod && !ignoreModLock)
        {
            instance.ModLock.ThrowIfCannotAdd();
            // Pack-Mods nicht durch eine andere Version ersetzen
            var existing = (await SyncAsync(instanceId, job.Token)).Mods.FirstOrDefault(m => m.ProjectId == request.ProjectId);
            if (existing != null)
                instance.ModLock.ThrowIfLocked(existing);
        }
        var provider = providers.Get(request.Source);
        var kind = ToKind(type);
        var mc = instance.Minecraft.Version;
        var loader = instance.Loader.Type;

        var doc = await SyncAsync(instanceId, job.Token);
        var installedProjects = Enum.GetValues<ContentType>().SelectMany(doc.ListOf)
            .Where(e => e.ProjectId != null).Select(e => e.ProjectId!).ToHashSet();

        // Abhängigkeiten auflösen (Breitensuche, höchstens 40 Projekte)
        var plan = new List<(ContentVersion Version, ContentType Type, bool IsDependency)>();
        var queue = new Queue<(string ProjectId, string? VersionId, ContentType Type, bool Dep)>();
        queue.Enqueue((request.ProjectId, request.VersionId, type, false));
        var seen = new HashSet<string>();
        while (queue.Count > 0 && plan.Count < 40)
        {
            var (projectId, versionId, itemType, isDep) = queue.Dequeue();
            if (!seen.Add(projectId) || (isDep && installedProjects.Contains(projectId)))
                continue;
            job.Status("content.resolving");
            var version = versionId != null
                ? await provider.GetVersionAsync(versionId, job.Token)
                : (await provider.GetVersionsAsync(projectId, mc, loader, ToKind(itemType), job.Token)).FirstOrDefault(v => v.Channel == "release")
                  ?? (await provider.GetVersionsAsync(projectId, mc, loader, ToKind(itemType), job.Token)).FirstOrDefault();
            if (version == null)
            {
                if (isDep)
                    continue;
                throw new AxoException("content.noCompatibleVersion", new() { ["version"] = mc });
            }
            plan.Add((version, itemType, isDep));
            if (itemType == ContentType.Mod)
                foreach (var dep in version.Dependencies.Where(d => d.Kind == DependencyKind.Required && d.ProjectId != null))
                    queue.Enqueue((dep.ProjectId!, dep.VersionId, ContentType.Mod, true));
        }

        var projects = await provider.GetProjectsAsync(plan.Select(p => p.Version.ProjectId), job.Token);
        var requests = new List<DownloadRequest>();
        var entries = new List<(ContentEntry Entry, ContentType Type)>();
        foreach (var (version, itemType, _) in plan)
        {
            var file = version.PrimaryFile ?? throw new AxoException("content.noFile");
            if (string.IsNullOrEmpty(file.Url))
                throw new AxoException("content.manualDownload", new() { ["name"] = projects.GetValueOrDefault(version.ProjectId)?.Title ?? file.FileName, ["projectId"] = version.ProjectId });
            if (!PathGuard.IsSafeFileName(file.FileName) || !IsContentFile(itemType, file.FileName))
                throw new AxoException("errors.invalidFileName");
            var target = PathGuard.Combine(Folder(instanceId, itemType), file.FileName);
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
                Dependencies = version.Dependencies.Where(d => d.Kind == DependencyKind.Required && d.ProjectId != null)
                    .Select(d => d.ProjectId!).ToList(),
                IconUrl = project?.IconUrl,
                Author = project?.Author is { Length: > 0 } a ? a : null
            }, itemType));
        }

        job.Status("content.downloading");
        await downloads.DownloadAllAsync(requests, job.Range(0.05, 0.95), job.Token);

        await ChangeAsync(instanceId, d =>
        {
            foreach (var (entry, itemType) in entries)
            {
                var list = d.ListOf(itemType);
                // Alte Datei desselben Projekts ersetzen (Update/Version ändern)
                foreach (var old in list.Where(e => e.ProjectId == entry.ProjectId && e.File != entry.File).ToList())
                {
                    TryDelete(Path.Combine(Folder(instanceId, itemType), old.DiskName));
                    list.Remove(old);
                }
                list.RemoveAll(e => e.File.Equals(entry.File, StringComparison.OrdinalIgnoreCase));
                var path = Path.Combine(Folder(instanceId, itemType), entry.File);
                var info = new FileInfo(path);
                entry.Modified = info.LastWriteTimeUtc;
                entry.Size = info.Length;
                if (itemType == ContentType.Mod)
                    entry.Meta = ModMetadataReader.Read(path)?.Meta;
                list.Add(entry);
            }
        }, job.Token);
        return entries.Select(e => e.Entry.File).ToList();
    }

    /// <summary>Lokale Datei hinzufügen (Drag &amp; Drop). Gilt als "Lokal · eigene Mod".</summary>
    public async Task<string> AddLocalFileAsync(string instanceId, ContentType type, string sourcePath, CancellationToken ct = default)
    {
        var name = Path.GetFileName(sourcePath);
        if (!PathGuard.IsSafeFileName(name) || !IsContentFile(type, name))
            throw new AxoException("content.wrongFileType");
        if (type == ContentType.Mod)
        {
            var modLock = (await instances.GetAsync(instanceId, ct)).ModLock;
            modLock.ThrowIfCannotAdd();
            if ((await SyncAsync(instanceId, ct)).Mods.FirstOrDefault(m => m.File.Equals(BaseName(name), StringComparison.OrdinalIgnoreCase)) is { } existing)
                modLock.ThrowIfLocked(existing);
        }
        var target = PathGuard.Combine(Folder(instanceId, type), name);
        await using (var source = File.OpenRead(sourcePath))
        await using (var dest = File.Create(target))
            await source.CopyToAsync(dest, ct);
        await SyncAsync(instanceId, ct);
        return name;
    }

    public async Task SetEnabledAsync(string instanceId, ContentType type, string file, bool enabled, CancellationToken ct = default)
    {
        var modLock = (await instances.GetAsync(instanceId, ct)).ModLock;
        await ChangeAsync(instanceId, doc =>
        {
            var entry = Find(doc, type, file);
            if (entry.Source == ContentSource.AxoClient)
                throw new AxoException("content.locked");
            if (type == ContentType.Mod)
                modLock.ThrowIfLocked(entry);
            // Nach dem tatsächlichen Stand auf der Platte richten, nicht nur nach content.json: sonst passiert nichts,
            // wenn beide auseinanderlaufen, oder es liegen "x.jar" und "x.jar.disabled" nebeneinander und die aktive lädt weiter.
            var dir = Folder(instanceId, type);
            var on = Path.Combine(dir, entry.File);
            var off = on + ".disabled";
            var to = enabled ? on : off;
            var from = File.Exists(enabled ? off : on) ? (enabled ? off : on) : null;
            if (from != null)
            {
                try
                {
                    File.Move(from, to, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Meist hält das laufende Spiel die Datei fest
                    throw new AxoException("content.fileInUse", new() { ["name"] = entry.Name }, ex);
                }
            }
            entry.Enabled = enabled;
        }, ct);
    }

    public async Task RemoveAsync(string instanceId, ContentType type, IReadOnlyCollection<string> files, CancellationToken ct = default)
    {
        var modLock = (await instances.GetAsync(instanceId, ct)).ModLock;
        await ChangeAsync(instanceId, doc =>
        {
            var list = doc.ListOf(type);
            // Erst alles prüfen, dann löschen: bei gemischter Auswahl nichts halb erledigen
            foreach (var file in files)
            {
                var entry = Find(doc, type, file);
                if (entry.Source == ContentSource.AxoClient)
                    throw new AxoException("content.locked");
                if (type == ContentType.Mod)
                    modLock.ThrowIfLocked(entry);
            }
            foreach (var file in files)
            {
                var entry = Find(doc, type, file);
                var path = Path.Combine(Folder(instanceId, type), entry.DiskName);
                if (File.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else if (Directory.Exists(Path.Combine(Folder(instanceId, type), entry.File)))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(Path.Combine(Folder(instanceId, type), entry.File),
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                list.Remove(entry);
            }
        }, ct);
    }

    /// <summary>Mod-Schutz umstellen. <paramref name="adoptCurrent"/>: alle jetzigen Mods zählen ab sofort zum Pack.</summary>
    public async Task<ModLock> SetModLockAsync(string instanceId, ModLockLevel level, bool adoptCurrent, CancellationToken ct = default)
    {
        var doc = await SyncAsync(instanceId, ct);
        var instance = await instances.GetAsync(instanceId, ct);
        instance.ModLock.SetLevel(level, doc.Mods, adoptCurrent);
        await instances.SaveAsync(instance, ct);
        events.Publish("content.changed", new { instanceId });
        return instance.ModLock;
    }

    /// <summary>Verfügbare Versionen für "Version ändern".</summary>
    public async Task<IReadOnlyList<ContentVersion>> VersionsForAsync(string instanceId, ContentType type, string file,
        bool allMinecraftVersions, CancellationToken ct = default)
    {
        var instance = await instances.GetAsync(instanceId, ct);
        var doc = await SyncAsync(instanceId, ct);
        var entry = Find(doc, type, file);
        if (entry.ProjectId == null || entry.Source is not (ContentSource.Modrinth or ContentSource.CurseForge))
            throw new AxoException("content.noSource");
        return await providers.Get(entry.Source).GetVersionsAsync(entry.ProjectId,
            allMinecraftVersions ? null : instance.Minecraft.Version, instance.Loader.Type, ToKind(type), ct);
    }

    public async Task ChangeAsync(string instanceId, Action<ContentDocument> change, CancellationToken ct = default)
    {
        var gate = LockFor(instanceId);
        await gate.WaitAsync(ct);
        try
        {
            await instances.ContentOf(instanceId).UpdateAsync(doc =>
            {
                change(doc);
                doc.ChangedAt = DateTimeOffset.UtcNow;
            }, ct);
        }
        finally
        {
            gate.Release();
        }
        events.Publish("content.changed", new { instanceId });
    }

    private static ContentEntry Find(ContentDocument doc, ContentType type, string file) =>
        doc.ListOf(type).FirstOrDefault(e => e.File.Equals(file, StringComparison.OrdinalIgnoreCase))
        ?? throw new AxoException("errors.notFound");

    public static ProjectKind ToKind(ContentType type) => type switch
    {
        ContentType.Mod => ProjectKind.Mod,
        ContentType.ResourcePack => ProjectKind.ResourcePack,
        _ => ProjectKind.ShaderPack
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException ex)
        {
            AppLog.Warn($"Alte Datei konnte nicht gelöscht werden: {Path.GetFileName(path)}", ex);
        }
    }

    public bool SupportsMods(Instance instance) => LoaderNames.SupportsMods(instance.Loader.Type);
}
