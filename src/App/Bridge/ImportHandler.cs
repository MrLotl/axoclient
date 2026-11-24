using System.IO.Compression;
using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.Import.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich Import.</summary>
    public class ImportHandler : BridgeHandler
    {
        private readonly IJobManager _jobs;
        private readonly IForeignScanner _foreign;
        private readonly IForeignImporter _foreignImport;
        private readonly IInstanceManager _instances;
        private readonly IInstanceTransfer _transfer;
        private readonly IGameLauncher _games;
        private readonly IInstanceBackup _backups;
        private readonly AppPaths _paths;
        private readonly FileRefs _files;
        private readonly IPathGuard _pathGuard;

        public ImportHandler(
            IJobManager jobs,
            IForeignScanner foreign,
            IForeignImporter foreignImport,
            IInstanceManager instances,
            IInstanceTransfer transfer,
            IGameLauncher games,
            IInstanceBackup backups,
            AppPaths paths,
            FileRefs files,
            IPathGuard pathGuard,
            IAppLog appLog,
            ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _jobs = jobs;
            _foreign = foreign;
            _foreignImport = foreignImport;
            _instances = instances;
            _transfer = transfer;
            _games = games;
            _backups = backups;
            _paths = paths;
            _files = files;
            _pathGuard = pathGuard;
        }

        // Entpackte Exporte fremder Launcher: Obergrenzen gegen ZIP-Bomben
        private const long MaxExtractBytes = 8L * 1024 * 1024 * 1024;
        private const int MaxExtractEntries = 200_000;

        public override void Register(MessageRouter router, MainWindow window)
        {
            // Andere Launcher an ihren Standard-Speicherorten durchsuchen
            router.On("import.scan", async _ =>
                await _jobs.Run("import.scan", new TextRef("import.scanTitle"), job => _foreign.Scan(job)));

            // Ausgewählte Instanzen übernehmen. Ergebnis pro Instanz (mit Hinweisen), die UI zeigt eine Zusammenfassung.
            router.On<ForeignImportData>("import.run", async (request, _) =>
            {
                if (request.Choices.Count == 0)
                {
                    throw new AxoException("errors.invalidInput");
                }

                var results = await _jobs.Run("import.run", new TextRef("import.runTitle", ("count", request.Choices.Count)),
                    job => _foreignImport.Import(request.Choices, job));
                await _instances.List();
                return results;
            });

            // In eine bestehende Instanz übernehmen: andere AxoClient-Instanzen als zusätzliche Quelle
            router.On<IdData>("import.ownSources", async (request, cancellationToken) => await _transfer.OwnSources(TextHelper.RequireId(request.Id), cancellationToken));

            router.On<TransferData>("import.into", async (request, _) =>
            {
                var target = TextHelper.RequireId(request.TargetId);
                if (_games.IsRunning(target))
                {
                    throw new AxoException("instances.running");
                }

                var result = await _jobs.Run("import.into", new TextRef("transfer.title"),
                    job => _transfer.Transfer(request.SourceId, target, request.Parts, job), target);
                await _instances.List();
                return result;
            });

            // Ordner einer Instanz (z. B. aus einem portablen Launcher) wählen
            router.On("import.folder", async _ =>
            {
                var folder = await window.OpenFolder("Instanzordner wählen");
                if (folder == null)
                {
                    return null;
                }

                return _foreign.FromFolder(folder, Path.GetFileName(folder.TrimEnd('\\', '/'))).ToView();
            });

            // Datei: .mrpack und AxoClient-Backups direkt; andere ZIPs (Prism/MultiMC-Export, CurseForge-Modpack) werden
            // entpackt und wie ein fremder Ordner gelesen – die UI zeigt sie dann zur Kontrolle wie eine gefundene Instanz.
            router.On<FileRefData>("import.file", async (request, _) =>
            {
                var path = _files.Resolve(request.FileRef);
                if (isDirectImport(path))
                {
                    var instance = await _jobs.Run("instance.import", new TextRef("instances.importing", ("file", Path.GetFileName(path))),
                        job => _backups.Import(path, job));
                    return new { kind = "instance", instance };
                }
                var folder = await _jobs.Run("import.extract", new TextRef("import.extracting", ("file", Path.GetFileName(path))),
                    job => extract(path, Path.Combine(_paths.Cache, "import", Guid.NewGuid().ToString("N")[..10]), job));
                var foreign = _foreign.FromFolder(folder, Path.GetFileNameWithoutExtension(path));
                return new { kind = "foreign", foreign = foreign.ToView() };
            });
        }

        private bool isDirectImport(string path)
        {
            if (path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                using var zip = ZipFile.OpenRead(path);
                return zip.GetEntry("modrinth.index.json") != null || zip.GetEntry("axoclient-backup.json") != null;
            }
            catch (InvalidDataException)
            {
                throw new AxoException("import.unknownFormat");
            }
        }

        /// <summary>Entpackt sicher (nur innerhalb des Zielordners) und steigt in einen einzelnen Oberordner hinein.</summary>
        private async Task<string> extract(string archive, string target, JobContext job)
        {
            Directory.CreateDirectory(target);
            using var zip = ZipFile.OpenRead(archive);
            if (zip.Entries.Count > MaxExtractEntries || zip.Entries.Sum(zipEntry => zipEntry.Length) > MaxExtractBytes)
            {
                throw new AxoException("import.tooLarge");
            }

            var files = zip.Entries.Where(zipEntry => !zipEntry.FullName.EndsWith('/')).ToList();
            for (var index = 0; index < files.Count; index++)
            {
                job.Token.ThrowIfCancellationRequested();
                var destination = _pathGuard.Combine(target, files[index].FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var from = files[index].Open())
                await using (var to = File.Create(destination))
                {
                    await from.CopyToAsync(to, job.Token);
                }

                job.Report((double)(index + 1) / files.Count);
            }
            var root = target;
            while (Directory.GetFiles(root).Length == 0 && Directory.GetDirectories(root) is [var only])
            {
                root = only;
            }

            return root;
        }
    }
}
