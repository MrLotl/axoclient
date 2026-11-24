using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Instances;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;

namespace AxoClient.Content
{
    /// <summary>Modpacks im Modrinth-Format: installieren, aktualisieren und eigene Instanzen exportieren.</summary>
    public sealed class ModpackManager : IModpackManager
    {
        private readonly AppPaths _paths;
        private readonly IInstanceManager _instances;
        private readonly IContentManager _content;
        private readonly IContentProviders _providers;
        private readonly IModrinthProvider _modrinth;
        private readonly IDownloadManager _downloads;
        private readonly IApiClient _api;
        private readonly ITextHelper _textHelper;
        private readonly IPathGuard _pathGuard;
        private readonly IAppLog _appLog;
        private readonly IContentFolders _contentFolders;
        private readonly IImageFormats _imageFormats;
        private readonly IGameVersionHelper _gameVersionHelper;
        /// <summary>Erlaubte Download-Hosts laut Modrinth-Spezifikation (Sicherheit).</summary>
        private readonly HashSet<string> _allowedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com", "objects.githubusercontent.com"
        };

        private readonly JsonSerializerOptions _indexJson = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public ModpackManager(
            AppPaths paths,
            IInstanceManager instances,
            IContentManager content,
            IContentProviders providers,
            IModrinthProvider modrinth,
            IDownloadManager downloads,
            IApiClient api,
            ITextHelper textHelper,
            IPathGuard pathGuard,
            IAppLog appLog,
            IContentFolders contentFolders,
            IImageFormats imageFormats,
            IGameVersionHelper gameVersionHelper)
        {
            _paths = paths;
            _instances = instances;
            _content = content;
            _providers = providers;
            _modrinth = modrinth;
            _downloads = downloads;
            _api = api;
            _textHelper = textHelper;
            _pathGuard = pathGuard;
            _appLog = appLog;
            _contentFolders = contentFolders;
            _imageFormats = imageFormats;
            _gameVersionHelper = gameVersionHelper;
        }

        /// <summary>Modpack von Modrinth als neue Instanz installieren.</summary>
        public async Task<Instance> InstallFromSource(string source, string projectId, string? versionId, JobContext job)
        {
            if (source != "modrinth")
            {
                throw new AxoException("content.sourceUnavailable", new() { ["source"] = source });
            }

            var project = await _modrinth.GetProject(projectId, job.Token);
            var version = versionId != null
                ? await _modrinth.GetVersion(versionId, job.Token) ?? throw new AxoException("errors.notFound")
                : (await _modrinth.GetVersions(projectId, null, null, ProjectKind.Modpack, job.Token)).FirstOrDefault()
                  ?? throw new AxoException("content.noCompatibleVersion", new() { ["version"] = "" });
            var file = version.Files.FirstOrDefault(f => f.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                       ?? throw new AxoException("content.noFile");

            job.Status("modpacks.downloading", ("name", project.Title));
            var packPath = Path.Combine(_paths.Tmp, $"{Guid.NewGuid():N}.mrpack");
            await _downloads.Download(new DownloadRequest(file.Url, packPath, file.Sha1, file.Sha512), job.Range(0, 0.1), job.Token);
            try
            {
                var instance = await ImportMrpack(packPath, job, project.Title);
                instance.Origin = new InstanceOrigin
                {
                    Type = OriginType.Modpack,
                    Source = "modrinth",
                    ProjectId = projectId,
                    VersionId = version.Id,
                    VersionName = version.VersionNumber
                };
                await _instances.Save(instance, job.Token);
                if (project.IconUrl != null)
                {
                    await trySetIcon(instance.Id, project.IconUrl, job.Token);
                }

                return instance;
            }
            finally
            {
                File.Delete(packPath);
            }
        }

        /// <summary>Eine <c>.mrpack</c>-Datei als neue Instanz einlesen.</summary>
        public async Task<Instance> ImportMrpack(string packPath, JobContext job, string? nameOverride = null)
        {
            using var zip = ZipFile.OpenRead(packPath);
            var index = ReadIndex(zip);
            var (mc, loader, loaderVersion) = ParseDependencies(index.Dependencies);
            var instance = await _instances.Create(new CreateInstanceRequest(nameOverride ?? _textHelper.Clean(index.Name, 60), mc, loader,
                loaderVersion), OriginType.Modpack, job.Token);
            try
            {
                await applyPack(instance.Id, zip, index, job, replaceExisting: false);
            }
            catch
            {
                await _instances.Delete(instance.Id);
                throw;
            }
            return instance;
        }

        private async Task applyPack(string instanceId, ZipArchive zip, MrpackIndex index, JobContext job, bool replaceExisting)
        {
            var gameDir = _instances.GameDir(instanceId);
            var requests = new List<DownloadRequest>();
            foreach (var file in index.Files)
            {
                if (file.Env != null && file.Env.TryGetValue("client", out var client) && client == "unsupported")
                {
                    continue;
                }

                var target = _pathGuard.Combine(gameDir, file.Path);
                var url = file.Downloads.FirstOrDefault(isAllowedDownload)
                          ?? throw new AxoException("modpacks.untrustedDownload", new() { ["file"] = Path.GetFileName(file.Path) });
                if (!file.Hashes.TryGetValue("sha1", out var sha1) && !file.Hashes.ContainsKey("sha512"))
                {
                    throw new AxoException("modpacks.missingHash", new() { ["file"] = Path.GetFileName(file.Path) });
                }

                requests.Add(new DownloadRequest(url, target, sha1, file.Hashes.GetValueOrDefault("sha512"),
                    file.FileSize > 0 ? file.FileSize : null));
            }

            job.Status("modpacks.downloadingFiles", ("count", requests.Count));
            await _downloads.DownloadAll(requests, job.Range(0.1, 0.85), job.Token);

            job.Status("modpacks.extracting");
            var overrides = zip.Entries.Where(e => !e.FullName.EndsWith('/') &&
                                                   (e.FullName.StartsWith("overrides/") || e.FullName.StartsWith("client-overrides/")))
                .OrderBy(e => e.FullName.StartsWith("client-overrides/") ? 1 : 0)
                .ToList();
            foreach (var entry in overrides)
            {
                job.Token.ThrowIfCancellationRequested();
                var relative = entry.FullName[(entry.FullName.IndexOf('/') + 1)..];
                var target = _pathGuard.Combine(gameDir, relative);
                if (!replaceExisting && File.Exists(target) && isUserData(relative))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var source = entry.Open();
                await using var dest = File.Create(target);
                await source.CopyToAsync(dest, job.Token);
            }

            // Herkunft der Dateien in content.json eintragen
            await _content.Sync(instanceId, job.Token);
            var bySha1 = index.Files.Where(f => f.Hashes.ContainsKey("sha1"))
                .ToDictionary(f => f.Hashes["sha1"], f => f, StringComparer.OrdinalIgnoreCase);
            var identified = await _modrinth.Identify(bySha1.Keys.Select(h => new LocalFileHash(h, null)).ToList(), job.Token);
            var projects = await _modrinth.GetProjects(identified.Values.Select(v => v.ProjectId), job.Token);
            await _content.Change(instanceId, doc =>
            {
                foreach (var entry in Enum.GetValues<ContentType>().SelectMany(doc.ListOf))
                {
                    if (entry.Hashes.Sha1 is not { } sha1 || !identified.TryGetValue(sha1, out var version))
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
                }
            }, job.Token);
            job.Report(1);
        }

        /// <summary>Welten, Screenshots und options.txt des Nutzers nicht überschreiben.</summary>
        private bool isUserData(string relative) =>
            relative.StartsWith("saves/") || relative.StartsWith("screenshots/") || relative == "options.txt" || relative == "servers.dat";

        /// <summary>Instanz auf eine neue Modpack-Version heben. Eigene Mods und Welten bleiben.</summary>
        public async Task Update(string instanceId, string versionId, JobContext job)
        {
            var instance = await _instances.Get(instanceId, job.Token);
            if (instance.Origin is not { Type: OriginType.Modpack, Source: "modrinth", ProjectId: { } projectId })
            {
                throw new AxoException("modpacks.notModpack");
            }

            var version = await _modrinth.GetVersion(versionId, job.Token) ?? throw new AxoException("errors.notFound");
            if (version.ProjectId != projectId)
            {
                throw new AxoException("errors.invalidInput");
            }

            var file = version.Files.FirstOrDefault(f => f.FileName.EndsWith(".mrpack")) ?? throw new AxoException("content.noFile");
            var packPath = Path.Combine(_paths.Tmp, $"{Guid.NewGuid():N}.mrpack");
            await _downloads.Download(new DownloadRequest(file.Url, packPath, file.Sha1, file.Sha512), job.Range(0, 0.1), job.Token);
            // Mod-Schutz gilt für Nutzer, nicht für das Pack-Update selbst: kurz lösen, danach mit den neuen Pack-Mods wieder setzen
            var lockLevel = instance.ModLock.Level;
            var ownMods = (await _content.Sync(instanceId, job.Token)).Mods.Where(m => !instance.ModLock.IsLocked(m)).Select(instance.ModLock.KeyOf).ToHashSet();
            if (lockLevel != ModLockLevel.Off)
            {
                instance.ModLock = new ModLock();
                await _instances.Save(instance, job.Token);
            }
            try
            {
                using var zip = ZipFile.OpenRead(packPath);
                var index = ReadIndex(zip);
                var (mc, loader, loaderVersion) = ParseDependencies(index.Dependencies);

                // Mods der alten Pack-Version entfernen, eigene behalten
                var doc = await _content.Sync(instanceId, job.Token);
                var oldPackFiles = doc.Mods.Where(m => m.Source == ContentSource.Modrinth).Select(m => m.File).ToList();
                if (oldPackFiles.Count > 0)
                {
                    await _content.Remove(instanceId, ContentType.Mod, oldPackFiles, job.Token);
                }

                await _instances.Update(instanceId, new InstanceUpdate(MinecraftVersion: mc, Loader: loader, LoaderVersion: loaderVersion),
                    job.Token);
                await applyPack(instanceId, zip, index, job, replaceExisting: true);
                instance = await _instances.Get(instanceId, job.Token);
                instance.Origin.VersionId = version.Id;
                instance.Origin.VersionName = version.VersionNumber;
                await _instances.Save(instance, job.Token);
            }
            finally
            {
                File.Delete(packPath);
                if (lockLevel != ModLockLevel.Off)
                {
                    var locked = await _content.SetModLock(instanceId, lockLevel, adoptCurrent: true, CancellationToken.None);
                    if (lockLevel == ModLockLevel.Pack)
                    {
                        var current = await _instances.Get(instanceId, CancellationToken.None);
                        current.ModLock.PackMods = locked.PackMods.Where(k => !ownMods.Contains(k)).ToList();
                        await _instances.Save(current, CancellationToken.None);
                    }
                }
            }
        }

        /// <summary>
        /// Instanz als <c>.mrpack</c> teilen: Inhalte von Modrinth als Verweis, alles andere (außer der AxoClient-Mod)
        /// als Overrides, dazu <c>config/</c> und <c>options.txt</c>. Keine Welten.
        /// </summary>
        public async Task ExportMrpack(string instanceId, string targetPath, JobContext job)
        {
            var instance = await _instances.Get(instanceId, job.Token);
            var doc = await _content.Sync(instanceId, job.Token);
            var gameDir = _instances.GameDir(instanceId);

            var modrinthEntries = Enum.GetValues<ContentType>()
                .SelectMany(t => doc.ListOf(t).Select(e => (Type: t, Entry: e)))
                .Where(x => x.Entry.Enabled && x.Entry.Source == ContentSource.Modrinth && x.Entry.VersionId != null)
                .ToList();
            var versions = await _modrinth.GetVersionsByIds(modrinthEntries.Select(x => x.Entry.VersionId!), job.Token);

            var index = new MrpackIndex
            {
                Name = instance.Name,
                VersionId = "1.0.0",
                Summary = "Exportiert mit AxoClient",
                Dependencies = buildDependencies(instance)
            };
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (type, entry) in modrinthEntries)
            {
                if (!versions.TryGetValue(entry.VersionId!, out var version) || version.Files.FirstOrDefault(f => f.FileName == entry.File) is not { } file)
                {
                    continue;
                }

                var relative = $"{_contentFolders.Of(type)}/{entry.File}";
                index.Files.Add(new MrpackFile
                {
                    Path = relative,
                    Hashes = new Dictionary<string, string> { ["sha1"] = file.Sha1 ?? entry.Hashes.Sha1 ?? "", ["sha512"] = file.Sha512 ?? "" },
                    Downloads = [file.Url],
                    FileSize = file.Size,
                    Env = new Dictionary<string, string> { ["client"] = "required", ["server"] = type == ContentType.Mod ? "optional" : "unsupported" }
                });
                referenced.Add(relative);
            }

            var tmp = targetPath + ".tmp";
            await using (var stream = File.Create(tmp))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var indexEntry = zip.CreateEntry("modrinth.index.json");
                await using (var writer = indexEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(writer, index, _indexJson, job.Token);
                }

                var overrideFiles = new List<string>();
                foreach (var type in Enum.GetValues<ContentType>())
                {
                    foreach (var entry in doc.ListOf(type).Where(e => e.Enabled && e.Source != ContentSource.AxoClient))
                    {
                        var relative = $"{_contentFolders.Of(type)}/{entry.File}";
                        if (!referenced.Contains(relative) && File.Exists(Path.Combine(gameDir, relative)))
                        {
                            overrideFiles.Add(relative);
                        }
                    }
                }

                if (Directory.Exists(Path.Combine(gameDir, "config")))
                {
                    overrideFiles.AddRange(Directory.EnumerateFiles(Path.Combine(gameDir, "config"), "*", SearchOption.AllDirectories)
                        .Where(f => !Path.GetFileName(f).StartsWith("axoclient", StringComparison.OrdinalIgnoreCase))
                        .Select(f => Path.GetRelativePath(gameDir, f).Replace('\\', '/')));
                }

                if (File.Exists(Path.Combine(gameDir, "options.txt")))
                {
                    overrideFiles.Add("options.txt");
                }

                for (var i = 0; i < overrideFiles.Count; i++)
                {
                    job.Token.ThrowIfCancellationRequested();
                    zip.CreateEntryFromFile(Path.Combine(gameDir, overrideFiles[i]), "overrides/" + overrideFiles[i], CompressionLevel.Optimal);
                    job.Report((double)(i + 1) / overrideFiles.Count);
                }
            }
            File.Move(tmp, targetPath, overwrite: true);
        }

        internal MrpackIndex ReadIndex(ZipArchive zip)
        {
            var entry = zip.GetEntry("modrinth.index.json") ?? throw new AxoException("modpacks.invalidPack");
            if (entry.Length > 8 * 1024 * 1024)
            {
                throw new AxoException("modpacks.invalidPack");
            }

            using var stream = entry.Open();
            var index = JsonSerializer.Deserialize<MrpackIndex>(stream, _indexJson) ?? throw new AxoException("modpacks.invalidPack");
            if (index.Game != "minecraft" || index.FormatVersion != 1)
            {
                throw new AxoException("modpacks.invalidPack");
            }

            return index;
        }

        internal (string Mc, LoaderType Loader, string? LoaderVersion) ParseDependencies(Dictionary<string, string> deps)
        {
            var mc = deps.GetValueOrDefault("minecraft") ?? throw new AxoException("modpacks.invalidPack");
            if (!_gameVersionHelper.IsSafeVersion(mc))
            {
                throw new AxoException("modpacks.invalidPack");
            }

            foreach (var (key, type) in new[]
                     {
                         ("fabric-loader", LoaderType.Fabric), ("quilt-loader", LoaderType.Quilt), ("forge", LoaderType.Forge),
                         ("neoforge", LoaderType.NeoForge)
                     })
            {
                if (deps.TryGetValue(key, out var version) && _gameVersionHelper.IsSafeVersion(version))
                {
                    return (mc, type, version);
                }
            }

            return (mc, LoaderType.Vanilla, null);
        }

        private Dictionary<string, string> buildDependencies(Instance instance)
        {
            var deps = new Dictionary<string, string> { ["minecraft"] = instance.Minecraft.Version };
            var key = instance.Loader.Type switch
            {
                LoaderType.Fabric => "fabric-loader",
                LoaderType.Quilt => "quilt-loader",
                LoaderType.Forge => "forge",
                LoaderType.NeoForge => "neoforge",
                _ => null
            };
            if (key != null && instance.Loader.Version != null)
            {
                deps[key] = instance.Loader.Version;
            }

            return deps;
        }

        private bool isAllowedDownload(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && _allowedHosts.Contains(uri.Host);

        private async Task trySetIcon(string instanceId, string url, CancellationToken ct)
        {
            try
            {
                using var response = await _api.Send(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (_imageFormats.IsPngOrJpeg(bytes))
                {
                    await _instances.SetImage(instanceId, false, bytes, ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or AxoException or IOException)
            {
                _appLog.Warn("Modpack-Icon konnte nicht geladen werden", ex);
            }
        }

        /// <summary>Für die Modpack-Seite: verfügbare Versionen eines Packs.</summary>
        public Task<IReadOnlyList<ContentVersion>> Versions(string source, string projectId, CancellationToken ct) =>
            _providers.Get(source).GetVersions(projectId, null, null, ProjectKind.Modpack, ct);
    }
}
