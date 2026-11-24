using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.Instances.Contract;

namespace AxoClient.Content
{
    /// <summary>
    /// Datapacks gehören zu einer Welt (<c>saves/&lt;welt&gt;/datapacks</c>), nicht zur Instanz. Minecraft lädt dort
    /// jede <c>.zip</c> und jeden Ordner mit <c>pack.mcmeta</c>; deaktiviert heißt hier <c>.zip.disabled</c>.
    /// Neu gefundene Packs schaltet Minecraft beim nächsten Laden der Welt selbst ein.
    /// </summary>
    public sealed class DatapackManager : IDatapackManager
    {
        private readonly AppPaths _paths;
        private readonly IInstanceManager _instances;
        private readonly IWorldManager _worlds;
        private readonly IContentProviders _providers;
        private readonly IDownloadManager _downloads;
        private readonly ITextHelper _textHelper;
        private readonly IPathGuard _pathGuard;
        private readonly IAppLog _appLog;
        private readonly IDirectoryHelper _directoryHelper;
        private readonly IJsonStoreFactory _jsonStoreFactory;
        private const string Disabled = ".disabled";

        // Eine Store-Instanz je Instanz, damit ihre Sperre gleichzeitige Änderungen ordnet
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, JsonStore<DatapackDocument>> _stores = new();

        public DatapackManager(
            AppPaths paths,
            IInstanceManager instances,
            IWorldManager worlds,
            IContentProviders providers,
            IDownloadManager downloads,
            ITextHelper textHelper,
            IPathGuard pathGuard,
            IAppLog appLog,
            IDirectoryHelper directoryHelper,
            IJsonStoreFactory jsonStoreFactory)
        {
            _paths = paths;
            _instances = instances;
            _worlds = worlds;
            _providers = providers;
            _downloads = downloads;
            _textHelper = textHelper;
            _pathGuard = pathGuard;
            _appLog = appLog;
            _directoryHelper = directoryHelper;
            _jsonStoreFactory = jsonStoreFactory;
        }

        private JsonStore<DatapackDocument> store(string instanceId) => _stores.GetOrAdd(_textHelper.RequireId(instanceId),
            id => _jsonStoreFactory.Create<DatapackDocument>(_paths.Fs, Path.Combine(_paths.Instance(id), "datapacks.json")));

        private string getFolder(string instanceId, string world) => Path.Combine(_worlds.WorldDirectory(instanceId, world), "datapacks");

        public async Task<IReadOnlyList<DatapackItem>> List(string instanceId, string world, CancellationToken cancellationToken = default)
        {
            var directory = getFolder(instanceId, world);
            var known = (await store(instanceId).Load(cancellationToken)).Worlds.GetValueOrDefault(world) ?? [];
            if (!Directory.Exists(directory))
            {
                return [];
            }

            var items = new List<DatapackItem>();
            foreach (var fileSystemEntry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                var isFolder = fileSystemEntry is DirectoryInfo;
                if (isFolder && !File.Exists(Path.Combine(fileSystemEntry.FullName, "pack.mcmeta")))
                {
                    continue;
                }

                bool enabled;
                string file;
                if (isFolder)
                {
                    (file, enabled) = (fileSystemEntry.Name, true);
                }
                else if (fileSystemEntry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    (file, enabled) = (fileSystemEntry.Name, true);
                }
                else if (fileSystemEntry.Name.EndsWith(".zip" + Disabled, StringComparison.OrdinalIgnoreCase))
                {
                    (file, enabled) = (fileSystemEntry.Name[..^Disabled.Length], false);
                }
                else
                {
                    continue;
                }

                var entry = known.FirstOrDefault(knownEntry => knownEntry.File.Equals(file, StringComparison.OrdinalIgnoreCase));
                var name = entry?.Name ?? (isFolder ? file : Path.GetFileNameWithoutExtension(file));
                items.Add(new DatapackItem(file, name, entry?.Version, enabled, isFolder, entry?.Source ?? ContentSource.Local, entry?.ProjectId,
                    entry?.IconUrl, entry?.Author, isFolder ? _directoryHelper.Size(fileSystemEntry.FullName) : ((FileInfo)fileSystemEntry).Length));
            }
            return items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Passende Version (Datapack-Loader, Minecraft-Version der Instanz) laden; ersetzt eine ältere desselben Projekts.</summary>
        public async Task<string> Install(string instanceId, string world, InstallRequest request, JobContext job)
        {
            var directory = getFolder(instanceId, world);
            var instance = await _instances.Get(instanceId, job.Token);
            var provider = _providers.Get(request.Source);
            job.Status("content.resolving");
            ContentVersion? version;
            if (request.VersionId != null)
            {
                version = await provider.GetVersion(request.VersionId, job.Token);
            }
            else
            {
                var list = await provider.GetVersions(request.ProjectId, instance.Minecraft.Version, null, ProjectKind.DataPack, job.Token);
                version = list.FirstOrDefault(candidate => candidate.Channel == "release") ?? list.FirstOrDefault();
            }
            if (version == null)
            {
                throw new AxoException("content.noCompatibleVersion", new() { ["version"] = instance.Minecraft.Version });
            }
            // Manche Projekte haben neben der Zip auch eine Mod-Jar: für die Welt zählt nur die Zip
            var file = version.Files.FirstOrDefault(candidateFile => candidateFile.Primary && isZip(candidateFile.FileName)) ?? version.Files.FirstOrDefault(candidateFile => isZip(candidateFile.FileName))
                       ?? throw new AxoException("datapacks.noZip");
            if (string.IsNullOrEmpty(file.Url))
            {
                throw new AxoException("content.manualDownload", new() { ["name"] = version.Name, ["projectId"] = version.ProjectId });
            }

            if (!_pathGuard.IsSafeFileName(file.FileName))
            {
                throw new AxoException("errors.invalidFileName");
            }

            Directory.CreateDirectory(directory);
            job.Status("content.downloading");
            await _downloads.DownloadAll([new DownloadRequest(file.Url, _pathGuard.Combine(directory, file.FileName), file.Sha1, file.Sha512,
                file.Size > 0 ? file.Size : null)], job.Range(0.05, 0.95), job.Token);

            var project = (await provider.GetProjects([version.ProjectId], job.Token)).GetValueOrDefault(version.ProjectId);
            await store(instanceId).Update(document =>
            {
                var entries = document.Worlds.TryGetValue(world, out var value) ? value : document.Worlds[world] = [];
                // Ältere Version desselben Projekts entfernen, sonst lädt Minecraft beide
                foreach (var old in entries.Where(entry => entry.ProjectId == version.ProjectId && !entry.File.Equals(file.FileName, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    tryDelete(Path.Combine(directory, old.File));
                    tryDelete(Path.Combine(directory, old.File + Disabled));
                    entries.Remove(old);
                }
                entries.RemoveAll(entry => entry.File.Equals(file.FileName, StringComparison.OrdinalIgnoreCase));
                entries.Add(new ContentEntry
                {
                    File = file.FileName,
                    Name = project?.Title ?? version.Name,
                    Version = version.VersionNumber,
                    Source = request.Source == "curseforge" ? ContentSource.CurseForge : ContentSource.Modrinth,
                    ProjectId = version.ProjectId,
                    VersionId = version.Id,
                    Hashes = new ContentHashes(file.Sha1, file.Sha512),
                    Size = file.Size,
                    IconUrl = project?.IconUrl,
                    Author = project?.Author is { Length: > 0 } author ? author : null
                });
            }, job.Token);
            return file.FileName;
        }

        public Task SetEnabled(string instanceId, string world, string file, bool enabled)
        {
            var directory = getFolder(instanceId, world);
            var name = requireFile(file);
            if (Directory.Exists(Path.Combine(directory, name)))
            {
                throw new AxoException("datapacks.folderToggle");
            }

            var on = Path.Combine(directory, name);
            var off = on + Disabled;
            var from = enabled ? off : on;
            if (!File.Exists(from))
            {
                return Task.CompletedTask;
            }

            try
            {
                File.Move(from, enabled ? on : off, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new AxoException("content.fileInUse", new() { ["name"] = name }, exception);
            }
            return Task.CompletedTask;
        }

        /// <summary>In den Papierkorb, wie bei Mods.</summary>
        public async Task Remove(string instanceId, string world, string file, CancellationToken cancellationToken = default)
        {
            var directory = getFolder(instanceId, world);
            var name = requireFile(file);
            var folder = Path.Combine(directory, name);
            if (Directory.Exists(folder))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            foreach (var path in new[] { folder, folder + Disabled }.Where(File.Exists))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            await store(instanceId).Update(document =>
            {
                if (document.Worlds.TryGetValue(world, out var entries))
                {
                    entries.RemoveAll(entry => entry.File.Equals(name, StringComparison.OrdinalIgnoreCase));
                }
            }, cancellationToken);
        }

        private bool isZip(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        private string requireFile(string? file) =>
            _pathGuard.IsSafeFileName(file) ? file! : throw new AxoException("errors.invalidFileName");

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
                _appLog.Warn($"Alter Datapack konnte nicht gelöscht werden: {Path.GetFileName(path)}", exception);
            }
        }
    }
}
