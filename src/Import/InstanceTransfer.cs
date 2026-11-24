using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Import;
using AxoClient.DataModel.Instances;
using AxoClient.Import.Contract;
using AxoClient.Instances.Contract;

namespace AxoClient.Import
{
    /// <summary>
    /// Übernimmt Teile einer anderen Instanz in eine bestehende – aus AxoClient selbst (Id <c>axo:&lt;id&gt;</c>) oder aus einem
    /// anderen Launcher (Id aus dem <see cref="ForeignScanner"/>). Nichts Vorhandenes geht verloren: Welten mit gleichem
    /// Namen bekommen einen neuen Namen, Einstellungen werden vorher als <c>.bak</c> gesichert, Server nur ergänzt.
    /// Passen Loader oder Version nicht, werden Modrinth-Mods der Quelle in der passenden Version neu installiert.
    /// </summary>
    public sealed class InstanceTransfer : IInstanceTransfer
    {
        private readonly IForeignScanner _scanner;
        private readonly IInstanceManager _instances;
        private readonly IContentManager _content;
        private readonly IServerListManager _servers;
        private readonly ITextHelper _textHelper;
        private readonly IAppLog _appLog;
        private readonly INbtReader _nbtReader;
        private readonly IServerPing _serverPing;
        private readonly IWorldManager _worlds;
        public const string OwnPrefix = "axo:";

        private readonly string[] _optionFiles = ["options.txt", "optionsof.txt", "optionsshaders.txt"];
        private readonly string[] _shaderConfigFiles = ["config/iris.properties", "config/oculus.properties"];

        public InstanceTransfer(
            IForeignScanner scanner,
            IInstanceManager instances,
            IContentManager content,
            IServerListManager servers,
            ITextHelper textHelper,
            IAppLog appLog,
            INbtReader nbtReader,
            IServerPing serverPing,
            IWorldManager worlds)
        {
            _scanner = scanner;
            _instances = instances;
            _content = content;
            _servers = servers;
            _textHelper = textHelper;
            _appLog = appLog;
            _nbtReader = nbtReader;
            _serverPing = serverPing;
            _worlds = worlds;
        }

        private sealed record Source(string Name, string GameDir, string? ModsDir, string? ConfigDir, LoaderType? Loader, string? Version,
            string? OwnId, string? ExtraDataDir);

        /// <summary>Andere AxoClient-Instanzen als Quelle (für die Auswahl im Dialog).</summary>
        public async Task<ForeignLauncherView> OwnSources(string exceptId, CancellationToken cancellationToken)
        {
            var list = (await _instances.List(cancellationToken)).Where(instanceSummary => instanceSummary.Id != exceptId).Select(instanceSummary => new ForeignInstanceView(
                OwnPrefix + instanceSummary.Id, instanceSummary.Name, instanceSummary.Loader, instanceSummary.MinecraftVersion, instanceSummary.LoaderVersion,
                _worlds.Count(Path.Combine(_instances.GameDirectory(instanceSummary.Id), "saves")), instanceSummary.ModCount, false, 0, null, null)).ToList();
            return new ForeignLauncherView("axoclient", "AxoClient", true, list, null);
        }

        public async Task<ForeignImportResult> Transfer(string sourceId, string targetId, ForeignImportParts parts, JobContext job)
        {
            var target = await _instances.Get(targetId, job.Token);
            var source = await resolve(sourceId, job.Token);
            var to = _instances.GameDirectory(target.Id);
            var notes = new List<TextRef>();

            if (parts.Worlds)
            {
                job.Status("transfer.worlds");
                var count = await Task.Run(() => copyWorlds(Path.Combine(source.GameDir, "saves"), Path.Combine(to, "saves")), job.Token);
                notes.Add(new TextRef("transfer.result.worlds", ("count", count)));
            }

            if (parts.Settings)
            {
                job.Status("transfer.settings");
                var copied = _optionFiles.Count(optionFile => copyWithBackup(Path.Combine(source.GameDir, optionFile), Path.Combine(to, optionFile)));
                notes.Add(new TextRef(copied > 0 ? "transfer.result.settings" : "transfer.result.noSettings"));
                var added = await mergeServers(Path.Combine(source.GameDir, "servers.dat"), target.Id);
                if (added > 0)
                {
                    notes.Add(new TextRef("transfer.result.servers", ("count", added)));
                }
            }

            if (parts.Packs)
            {
                job.Status("transfer.packs");
                var count = await Task.Run(() => copyMissing(Path.Combine(source.GameDir, "resourcepacks"), Path.Combine(to, "resourcepacks"))
                                                 + copyMissing(Path.Combine(source.GameDir, "shaderpacks"), Path.Combine(to, "shaderpacks")), job.Token);
                foreach (var file in _shaderConfigFiles)
                {
                    copyWithBackup(Path.Combine(source.GameDir, file), Path.Combine(to, file));
                }

                notes.Add(new TextRef("transfer.result.packs", ("count", count)));
            }

            if (parts.Screenshots)
            {
                var count = await Task.Run(() => copyMissing(Path.Combine(source.GameDir, "screenshots"), Path.Combine(to, "screenshots")), job.Token);
                notes.Add(new TextRef("transfer.result.screenshots", ("count", count)));
            }

            if (parts.Mods)
            {
                await transferMods(source, target, job, notes);
            }

            await _content.Sync(target.Id, job.Token);
            job.Report(1);
            return new ForeignImportResult(target.Name, target.Id, notes, null);
        }

        private async Task<Source> resolve(string id, CancellationToken cancellationToken)
        {
            if (id.StartsWith(OwnPrefix, StringComparison.Ordinal))
            {
                var own = await _instances.Get(_textHelper.RequireId(id[OwnPrefix.Length..]), cancellationToken);
                var directory = _instances.GameDirectory(own.Id);
                return new Source(own.Name, directory, Path.Combine(directory, "mods"), Path.Combine(directory, "config"), own.Loader.Type, own.Minecraft.Version, own.Id, null);
            }
            var foreign = _scanner.Get(id) ?? throw new AxoException("import.problem.gone");
            if (foreign.Problem != null)
            {
                throw new AxoException(foreign.Problem.Code);
            }

            return new Source(foreign.Name, foreign.GameDir, foreign.ModsDir, foreign.ExtraConfigDir ?? Path.Combine(foreign.GameDir, "config"),
                foreign.Loader, foreign.MinecraftVersion, null, foreign.ExtraDataDir);
        }

        private async Task transferMods(Source source, Instance target, JobContext job, List<TextRef> notes)
        {
            if (!target.ModLock.CanAdd())
            {
                notes.Add(new TextRef("transfer.result.modsLocked"));
                return;
            }
            if (target.Loader.Type == LoaderType.Vanilla)
            {
                notes.Add(new TextRef("transfer.result.modsVanilla"));
                return;
            }
            if (source.ModsDir == null || !Directory.Exists(source.ModsDir))
            {
                notes.Add(new TextRef("transfer.result.noMods"));
                return;
            }
            var to = _instances.GameDirectory(target.Id);
            var sameSetup = (source.Loader == null || source.Loader == target.Loader.Type)
                            && (source.Version == null || source.Version == target.Minecraft.Version);
            if (sameSetup)
            {
                job.Status("transfer.mods");
                var copied = await Task.Run(() => copyMissing(source.ModsDir, Path.Combine(to, "mods"), _scanner.IsModFile), job.Token);
                if (source.ConfigDir != null)
                {
                    await Task.Run(() => copyMissing(source.ConfigDir, Path.Combine(to, "config")), job.Token);
                }

                await _content.Sync(target.Id, job.Token);
                try
                {
                    await _content.IdentifyLocal(target.Id, job.Token);
                }
                catch (Exception exception) when (exception is HttpRequestException or AxoException)
                {
                    _appLog.Warn("Übernommene Mods konnten nicht zugeordnet werden", exception);
                }
                notes.Add(new TextRef("transfer.result.modsCopied", ("count", copied)));
                return;
            }

            // Andere Version oder anderer Loader: nur Mods mit bekannter Modrinth-Herkunft lassen sich passend neu holen
            if (source.OwnId == null)
            {
                notes.Add(new TextRef("transfer.result.modsMismatch", ("loader", source.Loader?.ToString() ?? "?"), ("version", source.Version ?? "?")));
                return;
            }
            var document = await _instances.ContentOf(source.OwnId).Load(job.Token);
            var installed = (await _content.Sync(target.Id, job.Token)).Mods.Where(mod => mod.ProjectId != null).Select(mod => mod.ProjectId!).ToHashSet();
            var known = document.Mods.Where(mod => mod.Source == ContentSource.Modrinth && mod.ProjectId != null && !installed.Contains(mod.ProjectId)).ToList();
            var unknown = document.Mods.Count(mod => mod.Source is ContentSource.Local or ContentSource.Friend);
            var failed = 0;
            for (var index = 0; index < known.Count; index++)
            {
                job.Token.ThrowIfCancellationRequested();
                job.Status("transfer.modsInstalling", ("index", index + 1), ("count", known.Count));
                try
                {
                    var files = await _content.Install(target.Id, ContentType.Mod, new InstallRequest("modrinth", known[index].ProjectId!),
                        job.Sub((double)index / known.Count, (double)(index + 1) / known.Count));
                    if (!known[index].Enabled && files.Count > 0)
                    {
                        await _content.SetEnabled(target.Id, ContentType.Mod, files[0], false, job.Token);
                    }
                }
                catch (Exception exception) when (exception is AxoException or HttpRequestException)
                {
                    failed++;
                }
            }
            notes.Add(new TextRef("transfer.result.modsReinstalled", ("count", known.Count - failed), ("version", target.Minecraft.Version)));
            if (failed > 0)
            {
                notes.Add(new TextRef("transfer.result.modsUnavailable", ("count", failed)));
            }

            if (unknown > 0)
            {
                notes.Add(new TextRef("transfer.result.modsLocal", ("count", unknown)));
            }
        }

        private async Task<int> mergeServers(string serversDat, string targetId)
        {
            if (!File.Exists(serversDat))
            {
                return 0;
            }

            NbtCompound root;
            try
            {
                root = _nbtReader.ReadFile(serversDat);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException)
            {
                return 0;
            }
            if (!root.TryGetValue("servers", out var value) || value is not NbtList list)
            {
                return 0;
            }

            var known = _servers.NormalizedAddresses(targetId).ToHashSet();
            var added = 0;
            foreach (var entry in list.OfType<NbtCompound>())
            {
                var address = _textHelper.Clean(entry.Get<string>("ip"), 255);
                if (address.Length == 0 || !_serverPing.IsValidAddress(address) || !known.Add(_serverPing.Normalize(address)))
                {
                    continue;
                }

                var name = _textHelper.Clean(entry.Get<string>("name"), 80);
                await _servers.Add(targetId, name.Length > 0 ? name : address, address);
                added++;
            }
            return added;
        }

        /// <summary>Kopiert alle Welten; gibt es den Ordnernamen schon, bekommt die Kopie " (2)", " (3)" … angehängt.</summary>
        private int copyWorlds(string from, string to)
        {
            if (!Directory.Exists(from))
            {
                return 0;
            }

            Directory.CreateDirectory(to);
            var count = 0;
            foreach (var world in Directory.EnumerateDirectories(from).Where(directory => File.Exists(Path.Combine(directory, "level.dat"))))
            {
                var name = Path.GetFileName(world);
                var destination = Path.Combine(to, name);
                for (var index = 2; Directory.Exists(destination); index++)
                {
                    destination = Path.Combine(to, $"{name} ({index})");
                }

                copyDirectory(world, destination);
                count++;
            }
            return count;
        }

        private bool copyWithBackup(string from, string to)
        {
            if (!File.Exists(from))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (File.Exists(to))
            {
                File.Copy(to, to + ".bak", overwrite: true);
            }

            using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(to, FileMode.Create, FileAccess.Write);
            input.CopyTo(output);
            return true;
        }

        /// <summary>Kopiert Dateien und Ordner, die im Ziel noch fehlen. Vorhandenes bleibt unangetastet.</summary>
        private int copyMissing(string from, string to, Func<string, bool>? fileFilter = null)
        {
            if (!Directory.Exists(from))
            {
                return 0;
            }

            Directory.CreateDirectory(to);
            var count = 0;
            foreach (var file in Directory.EnumerateFiles(from))
            {
                var destination = Path.Combine(to, Path.GetFileName(file));
                if ((fileFilter != null && !fileFilter(file)) || File.Exists(destination) || Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (tryCopy(file, destination))
                {
                    count++;
                }
            }
            if (fileFilter != null)
            {
                return count;
            }

            foreach (var directory in Directory.EnumerateDirectories(from).Where(directory => new DirectoryInfo(directory).LinkTarget == null))
            {
                var destination = Path.Combine(to, Path.GetFileName(directory));
                if (Directory.Exists(destination))
                {
                    continue;
                }

                copyDirectory(directory, destination);
                count++;
            }
            return count;
        }

        private void copyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from))
            {
                if (!Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                {
                    tryCopy(file, Path.Combine(to, Path.GetFileName(file)));
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(from).Where(directory => new DirectoryInfo(directory).LinkTarget == null))
            {
                copyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
            }
        }

        private bool tryCopy(string from, string to)
        {
            try
            {
                using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write);
                input.CopyTo(output);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _appLog.Warn($"Beim Übernehmen nicht kopierbar: {Path.GetFileName(from)}", exception);
                return false;
            }
        }
    }
}
