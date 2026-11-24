using System.IO.Compression;
using AxoClient.Common;
using AxoClient.DataModel.Instances;
using AxoClient.Instances.Contract;

namespace AxoClient.Instances
{
    /// <summary>Welten (<c>saves/*/level.dat</c>) und deren Backups in <c>backups/</c>.</summary>
    public sealed class WorldManager : IWorldManager
    {
        private readonly AppPaths _paths;
        private readonly ITextHelper _textHelper;
        private readonly IPathGuard _pathGuard;
        private readonly IAppLog _appLog;
        private readonly INbtReader _nbtReader;
        private readonly IDirectoryHelper _directoryHelper;

        public WorldManager(
            AppPaths paths,
            ITextHelper textHelper,
            IPathGuard pathGuard,
            IAppLog appLog,
            INbtReader nbtReader,
            IDirectoryHelper directoryHelper)
        {
            _paths = paths;
            _textHelper = textHelper;
            _pathGuard = pathGuard;
            _appLog = appLog;
            _nbtReader = nbtReader;
            _directoryHelper = directoryHelper;
        }

        private string savesDirectory(string id) => Path.Combine(_paths.GameDirectory(_textHelper.RequireId(id)), "saves");

        public Task<IReadOnlyList<WorldInfo>> List(string instanceId, CancellationToken cancellationToken = default) => Task.Run(() =>
        {
            var saves = savesDirectory(instanceId);
            if (!Directory.Exists(saves))
            {
                return (IReadOnlyList<WorldInfo>)[];
            }

            return Directory.EnumerateDirectories(saves)
                .Where(directory => File.Exists(Path.Combine(directory, "level.dat")))
                .Select(directory => read(instanceId, directory))
                .OrderByDescending(worldInfo => worldInfo.LastPlayed ?? DateTimeOffset.MinValue)
                .ToList();
        }, cancellationToken);

        public int Count(string savesDirectory) =>
            Directory.Exists(savesDirectory) ? Directory.EnumerateDirectories(savesDirectory).Count(directory => File.Exists(Path.Combine(directory, "level.dat"))) : 0;

        private WorldInfo read(string instanceId, string directory)
        {
            var folder = Path.GetFileName(directory);
            string name = folder, mode = "survival";
            string? version = null;
            bool hardcore = false, cheats = false;
            DateTimeOffset? lastPlayed = null;
            try
            {
                if (_nbtReader.ReadFile(Path.Combine(directory, "level.dat")).Get<NbtCompound>("Data") is { } data)
                {
                    name = data.Get<string>("LevelName") is { Length: > 0 } levelName ? _textHelper.Clean(levelName, 80) : folder;
                    if (data.TryGetValue("LastPlayed", out var lastPlayedValue) && lastPlayedValue is long milliseconds and > 0)
                    {
                        lastPlayed = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
                    }

                    mode = data.TryGetValue("GameType", out var gameType) && gameType is int type
                        ? type switch { 1 => "creative", 2 => "adventure", 3 => "spectator", _ => "survival" }
                        : "survival";
                    hardcore = data.TryGetValue("hardcore", out var hardcoreValue) && hardcoreValue is sbyte hardcoreFlag && hardcoreFlag != 0;
                    cheats = data.TryGetValue("allowCommands", out var allowCommands) && allowCommands is sbyte cheatsFlag && cheatsFlag != 0;
                    version = data.Get<NbtCompound>("Version")?.Get<string>("Name");
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException)
            {
                _appLog.Warn($"level.dat von {folder} nicht lesbar", exception);
            }
            var hasIcon = File.Exists(Path.Combine(directory, "icon.png"));
            return new WorldInfo(folder, name, mode, hardcore, cheats, version, lastPlayed, _directoryHelper.Size(directory),
                hasIcon ? $"/axo/world-icon/{instanceId}/{Uri.EscapeDataString(folder)}" : null);
        }

        public string? IconPath(string instanceId, string folder)
        {
            if (!_textHelper.IsValidId(instanceId))
            {
                return null;
            }

            var directory = _pathGuard.SafeCombine(savesDirectory(instanceId), folder);
            var icon = directory == null ? null : Path.Combine(directory, "icon.png");
            return icon != null && File.Exists(icon) ? icon : null;
        }

        public string WorldDirectory(string instanceId, string folder) =>
            _pathGuard.SafeCombine(savesDirectory(instanceId), folder) is { } directory && Directory.Exists(directory)
                ? directory
                : throw new AxoException("worlds.notFound");

        /// <summary>Welt als ZIP in <c>backups/</c> sichern.</summary>
        public async Task<BackupInfo> Backup(string instanceId, string folder, JobContext job)
        {
            var directory = WorldDirectory(instanceId, folder);
            var backups = _paths.InstanceBackups(instanceId);
            Directory.CreateDirectory(backups);
            var file = $"{folder}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
            var target = Path.Combine(backups, file);
            var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(filePath => !Path.GetFileName(filePath).Equals("session.lock", StringComparison.OrdinalIgnoreCase)).ToList();
            await using (var stream = File.Create(target + ".tmp"))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                for (var index = 0; index < files.Count; index++)
                {
                    job.Token.ThrowIfCancellationRequested();
                    var entry = zip.CreateEntry(folder + "/" + Path.GetRelativePath(directory, files[index]).Replace('\\', '/'),
                        CompressionLevel.Fastest);
                    await using var source = new FileStream(files[index], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    await using var target2 = entry.Open();
                    await source.CopyToAsync(target2, job.Token);
                    job.Report((double)(index + 1) / files.Count);
                }
            }
            File.Move(target + ".tmp", target, overwrite: true);
            return new BackupInfo(file, folder, DateTimeOffset.Now, new FileInfo(target).Length);
        }

        /// <summary>
        /// Alle Welten sichern, z. B. vor einem Wechsel von Version oder Loader: Minecraft wandelt Welten beim ersten Öffnen
        /// um, und fehlende Mods entfernen ihre Blöcke. Bricht ab, bevor das Laufwerk vollläuft.
        /// </summary>
        public async Task<int> BackupAll(string instanceId, JobContext job)
        {
            var saves = savesDirectory(instanceId);
            var worlds = Directory.Exists(saves)
                ? Directory.EnumerateDirectories(saves).Where(directory => File.Exists(Path.Combine(directory, "level.dat"))).ToList()
                : [];
            if (worlds.Count == 0)
            {
                return 0;
            }

            var backups = _paths.InstanceBackups(instanceId);
            Directory.CreateDirectory(backups);
            // ZIP ist meist kleiner als die Welt; die volle Größe plus Puffer ist auf der sicheren Seite
            var needed = worlds.Sum(_directoryHelper.Size) + (512L << 20);
            var root = Path.GetPathRoot(Path.GetFullPath(backups));
            if (root != null && new DriveInfo(root).AvailableFreeSpace < needed)
            {
                throw new AxoException(new TextRef("worlds.backupNoSpace", ("gb", Math.Ceiling(needed / 1e8) / 10)));
            }

            for (var index = 0; index < worlds.Count; index++)
            {
                job.Status("worlds.backingUpOf", ("index", index + 1), ("count", worlds.Count));
                await Backup(instanceId, Path.GetFileName(worlds[index]), job.Sub((double)index / worlds.Count, (double)(index + 1) / worlds.Count));
            }
            return worlds.Count;
        }

        public IReadOnlyList<BackupInfo> ListBackups(string instanceId)
        {
            var directory = _paths.InstanceBackups(_textHelper.RequireId(instanceId));
            if (!Directory.Exists(directory))
            {
                return [];
            }

            return new DirectoryInfo(directory).EnumerateFiles("*.zip")
                .Select(file => new BackupInfo(file.Name, file.Name.Contains('-') ? file.Name[..file.Name.LastIndexOf('-', file.Name.LastIndexOf('_'))] : file.Name,
                    file.CreationTime, file.Length))
                .OrderByDescending(backupInfo => backupInfo.CreatedAt)
                .ToList();
        }

        /// <summary>Backup wiederherstellen. Gibt es die Welt schon, landet sie unter neuem Namen daneben.</summary>
        public async Task<string> RestoreBackup(string instanceId, string backupFile, JobContext job)
        {
            var zipPath = _pathGuard.Combine(_paths.InstanceBackups(_textHelper.RequireId(instanceId)), backupFile);
            var saves = savesDirectory(instanceId);
            Directory.CreateDirectory(saves);
            using var zip = ZipFile.OpenRead(zipPath);
            var root = zip.Entries.Select(zipEntry => zipEntry.FullName.Split('/')[0]).FirstOrDefault() ?? "Welt";
            var target = root;
            for (var index = 2; Directory.Exists(Path.Combine(saves, target)); index++)
            {
                target = $"{root} ({index})";
            }

            var count = zip.Entries.Count;
            var done = 0;
            foreach (var entry in zip.Entries)
            {
                job.Token.ThrowIfCancellationRequested();
                var relative = target + entry.FullName[root.Length..];
                var path = _pathGuard.Combine(saves, relative.TrimEnd('/'));
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(path);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using var source = entry.Open();
                    await using var file = File.Create(path);
                    await source.CopyToAsync(file, job.Token);
                }
                job.Report((double)++done / count);
            }
            return target;
        }

        /// <summary>
        /// Welt aus einer ZIP übernehmen (Download oder Export). Die Welt ist der Ordner mit der obersten
        /// <c>level.dat</c>; gibt es den Namen schon, landet sie unter neuem Namen daneben.
        /// </summary>
        public async Task<string> Import(string instanceId, string zipPath, JobContext job)
        {
            var saves = savesDirectory(instanceId);
            Directory.CreateDirectory(saves);
            using var zip = ZipFile.OpenRead(zipPath);
            var levelDat = zip.Entries.Where(zipEntry => zipEntry.FullName.Replace('\\', '/').Split('/')[^1] == "level.dat")
                .MinBy(zipEntry => zipEntry.FullName.Count(character => character == '/')) ?? throw new AxoException("worlds.importNoWorld");
            var prefix = levelDat.FullName.Replace('\\', '/')[..^"level.dat".Length];
            var name = prefix.TrimEnd('/').Split('/')[^1] is { Length: > 0 } folder ? folder : Path.GetFileNameWithoutExtension(zipPath);
            name = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.') is { Length: > 0 } clean ? clean : "Welt";
            var target = name;
            for (var index = 2; Directory.Exists(Path.Combine(saves, target)); index++)
            {
                target = $"{name} ({index})";
            }

            var entries = zip.Entries.Where(zipEntry => zipEntry.FullName.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal)).ToList();
            var done = 0;
            try
            {
                foreach (var entry in entries)
                {
                    job.Token.ThrowIfCancellationRequested();
                    var relative = entry.FullName.Replace('\\', '/')[prefix.Length..];
                    if (relative.Length == 0)
                    {
                        continue;
                    }

                    var path = _pathGuard.Combine(saves, target + "/" + relative.TrimEnd('/'));
                    if (relative.EndsWith('/'))
                    {
                        Directory.CreateDirectory(path);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await using var source = entry.Open();
                        await using var file = File.Create(path);
                        await source.CopyToAsync(file, job.Token);
                    }
                    job.Report((double)++done / entries.Count);
                }
            }
            catch
            {
                // Halbe Welt nicht liegen lassen
                try
                {
                    Directory.Delete(Path.Combine(saves, target), recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
                throw;
            }
            return target;
        }

        public async Task Delete(string instanceId, string folder, CancellationToken cancellationToken = default)
        {
            var directory = WorldDirectory(instanceId, folder);
            await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(directory,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin), cancellationToken);
        }
    }
}
