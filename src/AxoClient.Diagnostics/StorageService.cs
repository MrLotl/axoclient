using AxoClient.Common;
using AxoClient.DataModel.Diagnostics;
using AxoClient.Diagnostics.Contract;
using AxoClient.Instances.Contract;

namespace AxoClient.Diagnostics
{
    /// <summary>Speicher-Übersicht in den Einstellungen: wie viel jeder Bereich belegt, mit Aufräum-Knöpfen.</summary>
    public sealed class StorageService : IStorageService
    {
        private readonly AppPaths _paths;
        private readonly IInstanceManager _instances;
        private readonly IAppLog _appLog;
        private readonly IDirectoryHelper _directoryHelper;

        public StorageService(AppPaths paths, IInstanceManager instances, IAppLog appLog, IDirectoryHelper directoryHelper)
        {
            _paths = paths;
            _instances = instances;
            _appLog = appLog;
            _directoryHelper = directoryHelper;
        }

        public async Task<StorageOverview> Scan(CancellationToken ct)
        {
            var list = new List<InstanceStorage>();
            long backups = 0;
            foreach (var instance in await _instances.LoadAll(ct))
            {
                var game = _paths.GameDir(instance.Id);
                var worlds = _directoryHelper.Size(Path.Combine(game, "saves"));
                var mods = _directoryHelper.Size(Path.Combine(game, "mods"));
                var all = _directoryHelper.Size(_paths.Instance(instance.Id));
                var instanceBackups = _directoryHelper.Size(_paths.InstanceBackups(instance.Id));
                backups += instanceBackups;
                list.Add(new InstanceStorage(instance.Id, instance.Name, worlds, mods, Math.Max(0, all - worlds - mods - instanceBackups)));
            }
            var gameSize = _directoryHelper.Size(_paths.Game);
            var java = _directoryHelper.Size(_paths.Java);
            var cache = _directoryHelper.Size(_paths.Cache) + _directoryHelper.Size(_paths.Tmp);
            var logs = _directoryHelper.Size(_paths.Logs);
            var servers = _directoryHelper.Size(_paths.Servers);
            var total = list.Sum(i => i.Worlds + i.Mods + i.Rest) + backups + gameSize + java + cache + logs + servers;
            return new StorageOverview(list.OrderByDescending(i => i.Worlds + i.Mods + i.Rest).ToList(), backups, gameSize, java, cache, logs,
                servers, total);
        }

        /// <summary>Welt-Backups älter als <paramref name="keepDays"/> Tage löschen; das jeweils neueste pro Welt bleibt.</summary>
        public long CleanBackups(int keepDays = 30)
        {
            long freed = 0;
            foreach (var id in _instances.Ids())
            {
                var dir = _paths.InstanceBackups(id);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                var files = new DirectoryInfo(dir).EnumerateFiles("*.zip").OrderByDescending(f => f.CreationTimeUtc).ToList();
                var newestPerWorld = files.GroupBy(f => f.Name[..Math.Max(1, f.Name.LastIndexOf('-', Math.Max(0, f.Name.LastIndexOf('_'))))])
                    .Select(g => g.First().FullName).ToHashSet();
                foreach (var file in files.Where(f => f.CreationTimeUtc < DateTime.UtcNow.AddDays(-keepDays) && !newestPerWorld.Contains(f.FullName)))
                {
                    freed += file.Length;
                    file.Delete();
                }
            }
            return freed;
        }

        /// <summary>Minecraft-Versionen entfernen, die keine Instanz mehr nutzt (Libraries und Assets bleiben).</summary>
        public async Task<long> CleanVersions(CancellationToken ct)
        {
            if (!Directory.Exists(_paths.Versions))
            {
                return 0;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var instance in await _instances.LoadAll(ct))
            {
                used.Add(instance.Minecraft.Version);
                if (instance.Loader.Version is { } lv)
                {
                    used.UnionWith(Directory.EnumerateDirectories(_paths.Versions).Select(Path.GetFileName).OfType<string>()
                        .Where(n => n.Contains(lv) && n.Contains(instance.Minecraft.Version)));
                }
            }
            long freed = 0;
            foreach (var dir in Directory.EnumerateDirectories(_paths.Versions))
            {
                if (used.Contains(Path.GetFileName(dir)))
                {
                    continue;
                }

                freed += _directoryHelper.Size(dir);
                Directory.Delete(dir, recursive: true);
            }
            return freed;
        }

        /// <summary>Java-Runtimes entfernen, die keine Version mehr braucht (beim nächsten Start lädt CmlLib bei Bedarf neu).</summary>
        public long CleanJava()
        {
            long freed = 0;
            foreach (var dir in Directory.Exists(_paths.Java) ? Directory.EnumerateDirectories(_paths.Java).ToList() : [])
            {
                freed += _directoryHelper.Size(dir);
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException)
                {
                    // Gerade benutzt
                }
            }
            return freed;
        }

        public long CleanCache()
        {
            long freed = 0;
            foreach (var dir in new[] { _paths.ApiCache, _paths.DownloadCache, _paths.ServerIcons, _paths.Tmp })
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        freed += file.Length;
                        file.Delete();
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            return freed;
        }

        public long CleanLogs()
        {
            long freed = 0;
            foreach (var file in Directory.Exists(_paths.Logs) ? new DirectoryInfo(_paths.Logs).EnumerateFiles().ToList() : [])
            {
                if (file.Name == Path.GetFileName(_appLog.CurrentFile))
                {
                    continue;
                }

                freed += file.Length;
                file.Delete();
            }
            return freed;
        }
    }
}
