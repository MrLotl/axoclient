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
            var file = version.Files.FirstOrDefault(candidateFile => candidateFile.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
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
            var (minecraftVersion, loader, loaderVersion) = ParseDependencies(index.Dependencies);
            var instance = await _instances.Create(new CreateInstanceRequest(nameOverride ?? _textHelper.Clean(index.Name, 60), minecraftVersion, loader,
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
            var gameDirectory = _instances.GameDirectory(instanceId);
            var requests = new List<DownloadRequest>();
            foreach (var file in index.Files)
            {
                if (file.Env != null && file.Env.TryGetValue("client", out var client) && client == "unsupported")
                {
                    continue;
                }

                var target = _pathGuard.Combine(gameDirectory, file.Path);
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
            var overrides = zip.Entries.Where(zipEntry => !zipEntry.FullName.EndsWith('/') &&
                                                   (zipEntry.FullName.StartsWith("overrides/") || zipEntry.FullName.StartsWith("client-overrides/")))
                .OrderBy(zipEntry => zipEntry.FullName.StartsWith("client-overrides/") ? 1 : 0)
                .ToList();
            foreach (var entry in overrides)
            {
                job.Token.ThrowIfCancellationRequested();
                var relative = entry.FullName[(entry.FullName.IndexOf('/') + 1)..];
                var target = _pathGuard.Combine(gameDirectory, relative);
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
            var bySha1 = index.Files.Where(file => file.Hashes.ContainsKey("sha1"))
                .ToDictionary(file => file.Hashes["sha1"], file => file, StringComparer.OrdinalIgnoreCase);
            var identified = await _modrinth.Identify(bySha1.Keys.Select(key => new LocalFileHash(key, null)).ToList(), job.Token);
            var projects = await _modrinth.GetProjects(identified.Values.Select(value => value.ProjectId), job.Token);
            await _content.Change(instanceId, document =>
            {
                foreach (var entry in Enum.GetValues<ContentType>().SelectMany(document.ListOf))
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

            var file = version.Files.FirstOrDefault(candidateFile => candidateFile.FileName.EndsWith(".mrpack")) ?? throw new AxoException("content.noFile");
            var packPath = Path.Combine(_paths.Tmp, $"{Guid.NewGuid():N}.mrpack");
            await _downloads.Download(new DownloadRequest(file.Url, packPath, file.Sha1, file.Sha512), job.Range(0, 0.1), job.Token);
            // Mod-Schutz gilt für Nutzer, nicht für das Pack-Update selbst: kurz lösen, danach mit den neuen Pack-Mods wieder setzen
            var lockLevel = instance.ModLock.Level;
            var ownMods = (await _content.Sync(instanceId, job.Token)).Mods.Where(mod => !instance.ModLock.IsLocked(mod)).Select(instance.ModLock.KeyOf).ToHashSet();
            if (lockLevel != ModLockLevel.Off)
            {
                instance.ModLock = new ModLock();
                await _instances.Save(instance, job.Token);
            }
            try
            {
                using var zip = ZipFile.OpenRead(packPath);
                var index = ReadIndex(zip);
                var (minecraftVersion, loader, loaderVersion) = ParseDependencies(index.Dependencies);

                // Mods der alten Pack-Version entfernen, eigene behalten
                var document = await _content.Sync(instanceId, job.Token);
                var oldPackFiles = document.Mods.Where(mod => mod.Source == ContentSource.Modrinth).Select(mod => mod.File).ToList();
                if (oldPackFiles.Count > 0)
                {
                    await _content.Remove(instanceId, ContentType.Mod, oldPackFiles, job.Token);
                }

                await _instances.Update(instanceId, new InstanceUpdate(MinecraftVersion: minecraftVersion, Loader: loader, LoaderVersion: loaderVersion),
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
                        current.ModLock.PackMods = locked.PackMods.Where(packMod => !ownMods.Contains(packMod)).ToList();
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
            var document = await _content.Sync(instanceId, job.Token);
            var gameDirectory = _instances.GameDirectory(instanceId);

            var modrinthEntries = Enum.GetValues<ContentType>()
                .SelectMany(value => document.ListOf(value).Select(entry => (Type: value, Entry: entry)))
                .Where(contentItem => contentItem.Entry.Enabled && contentItem.Entry.Source == ContentSource.Modrinth && contentItem.Entry.VersionId != null)
                .ToList();
            var versions = await _modrinth.GetVersionsByIds(modrinthEntries.Select(modrinthEntry => modrinthEntry.Entry.VersionId!), job.Token);

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
                if (!versions.TryGetValue(entry.VersionId!, out var version) || version.Files.FirstOrDefault(candidateFile => candidateFile.FileName == entry.File) is not { } file)
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

            var temporary = targetPath + ".tmp";
            await using (var stream = File.Create(temporary))
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
                    foreach (var entry in document.ListOf(type).Where(entry => entry.Enabled && entry.Source != ContentSource.AxoClient))
                    {
                        var relative = $"{_contentFolders.Of(type)}/{entry.File}";
                        if (!referenced.Contains(relative) && File.Exists(Path.Combine(gameDirectory, relative)))
                        {
                            overrideFiles.Add(relative);
                        }
                    }
                }

                if (Directory.Exists(Path.Combine(gameDirectory, "config")))
                {
                    overrideFiles.AddRange(Directory.EnumerateFiles(Path.Combine(gameDirectory, "config"), "*", SearchOption.AllDirectories)
                        .Where(file => !Path.GetFileName(file).StartsWith("axoclient", StringComparison.OrdinalIgnoreCase))
                        .Select(file => Path.GetRelativePath(gameDirectory, file).Replace('\\', '/')));
                }

                if (File.Exists(Path.Combine(gameDirectory, "options.txt")))
                {
                    overrideFiles.Add("options.txt");
                }

                for (var fileIndex = 0; fileIndex < overrideFiles.Count; fileIndex++)
                {
                    job.Token.ThrowIfCancellationRequested();
                    zip.CreateEntryFromFile(Path.Combine(gameDirectory, overrideFiles[fileIndex]), "overrides/" + overrideFiles[fileIndex], CompressionLevel.Optimal);
                    job.Report((double)(fileIndex + 1) / overrideFiles.Count);
                }
            }
            File.Move(temporary, targetPath, overwrite: true);
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

        internal (string Mc, LoaderType Loader, string? LoaderVersion) ParseDependencies(Dictionary<string, string> dependencies)
        {
            var minecraftVersion = dependencies.GetValueOrDefault("minecraft") ?? throw new AxoException("modpacks.invalidPack");
            if (!_gameVersionHelper.IsSafeVersion(minecraftVersion))
            {
                throw new AxoException("modpacks.invalidPack");
            }

            foreach (var (key, type) in new[]
                     {
                         ("fabric-loader", LoaderType.Fabric), ("quilt-loader", LoaderType.Quilt), ("forge", LoaderType.Forge),
                         ("neoforge", LoaderType.NeoForge)
                     })
            {
                if (dependencies.TryGetValue(key, out var version) && _gameVersionHelper.IsSafeVersion(version))
                {
                    return (minecraftVersion, type, version);
                }
            }

            return (minecraftVersion, LoaderType.Vanilla, null);
        }

        private Dictionary<string, string> buildDependencies(Instance instance)
        {
            var dependencies = new Dictionary<string, string> { ["minecraft"] = instance.Minecraft.Version };
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
                dependencies[key] = instance.Loader.Version;
            }

            return dependencies;
        }

        private bool isAllowedDownload(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && _allowedHosts.Contains(uri.Host);

        private async Task trySetIcon(string instanceId, string url, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _api.Send(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (_imageFormats.IsPngOrJpeg(bytes))
                {
                    await _instances.SetImage(instanceId, false, bytes, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or AxoException or IOException)
            {
                _appLog.Warn("Modpack-Icon konnte nicht geladen werden", exception);
            }
        }

        /// <summary>Für die Modpack-Seite: verfügbare Versionen eines Packs.</summary>
        public Task<IReadOnlyList<ContentVersion>> Versions(string source, string projectId, CancellationToken cancellationToken) =>
            _providers.Get(source).GetVersions(projectId, null, null, ProjectKind.Modpack, cancellationToken);
    }
}
