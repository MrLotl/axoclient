using AxoClient.Core.Common;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Diagnostics;

public sealed record InstanceStorage(string Id, string Name, long Worlds, long Mods, long Rest);

public sealed record StorageOverview(
    IReadOnlyList<InstanceStorage> Instances,
    long Backups,
    long Game,
    long Java,
    long Cache,
    long Logs,
    long Servers,
    long Total);

/// <summary>Speicher-Übersicht in den Einstellungen: wie viel jeder Bereich belegt, mit Aufräum-Knöpfen.</summary>
public sealed class StorageService(AppPaths paths, InstanceManager instances)
{
    public async Task<StorageOverview> ScanAsync(CancellationToken ct)
    {
        var list = new List<InstanceStorage>();
        long backups = 0;
        foreach (var instance in await instances.LoadAllAsync(ct))
        {
            var game = paths.GameDir(instance.Id);
            var worlds = WorldManager.DirectorySize(Path.Combine(game, "saves"));
            var mods = WorldManager.DirectorySize(Path.Combine(game, "mods"));
            var all = WorldManager.DirectorySize(paths.Instance(instance.Id));
            var instanceBackups = WorldManager.DirectorySize(paths.InstanceBackups(instance.Id));
            backups += instanceBackups;
            list.Add(new InstanceStorage(instance.Id, instance.Name, worlds, mods, Math.Max(0, all - worlds - mods - instanceBackups)));
        }
        var gameSize = WorldManager.DirectorySize(paths.Game);
        var java = WorldManager.DirectorySize(paths.Java);
        var cache = WorldManager.DirectorySize(paths.Cache) + WorldManager.DirectorySize(paths.Tmp);
        var logs = WorldManager.DirectorySize(paths.Logs);
        var servers = WorldManager.DirectorySize(paths.Servers);
        var total = list.Sum(i => i.Worlds + i.Mods + i.Rest) + backups + gameSize + java + cache + logs + servers;
        return new StorageOverview(list.OrderByDescending(i => i.Worlds + i.Mods + i.Rest).ToList(), backups, gameSize, java, cache, logs,
            servers, total);
    }

    /// <summary>Welt-Backups älter als <paramref name="keepDays"/> Tage löschen; das jeweils neueste pro Welt bleibt.</summary>
    public long CleanBackups(int keepDays = 30)
    {
        long freed = 0;
        foreach (var id in instances.Ids())
        {
            var dir = paths.InstanceBackups(id);
            if (!Directory.Exists(dir))
                continue;
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
    public async Task<long> CleanVersionsAsync(CancellationToken ct)
    {
        if (!Directory.Exists(paths.Versions))
            return 0;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in await instances.LoadAllAsync(ct))
        {
            used.Add(instance.Minecraft.Version);
            if (instance.Loader.Version is { } lv)
                used.UnionWith(Directory.EnumerateDirectories(paths.Versions).Select(Path.GetFileName).OfType<string>()
                    .Where(n => n.Contains(lv) && n.Contains(instance.Minecraft.Version)));
        }
        long freed = 0;
        foreach (var dir in Directory.EnumerateDirectories(paths.Versions))
        {
            if (used.Contains(Path.GetFileName(dir)))
                continue;
            freed += WorldManager.DirectorySize(dir);
            Directory.Delete(dir, recursive: true);
        }
        return freed;
    }

    /// <summary>Java-Runtimes entfernen, die keine Version mehr braucht (beim nächsten Start lädt CmlLib bei Bedarf neu).</summary>
    public long CleanJava()
    {
        long freed = 0;
        foreach (var dir in Directory.Exists(paths.Java) ? Directory.EnumerateDirectories(paths.Java).ToList() : [])
        {
            freed += WorldManager.DirectorySize(dir);
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
        foreach (var dir in new[] { paths.ApiCache, paths.DownloadCache, paths.ServerIcons, paths.Tmp })
        {
            if (!Directory.Exists(dir))
                continue;
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
        foreach (var file in Directory.Exists(paths.Logs) ? new DirectoryInfo(paths.Logs).EnumerateFiles().ToList() : [])
        {
            if (file.Name == Path.GetFileName(AppLog.CurrentFile))
                continue;
            freed += file.Length;
            file.Delete();
        }
        return freed;
    }
}
