using System.IO.Compression;
using AxoClient.Core.Common;

namespace AxoClient.Core.Instances;

public sealed record WorldInfo(
    string Folder,
    string Name,
    string GameMode,
    bool Hardcore,
    bool Cheats,
    string? Version,
    DateTimeOffset? LastPlayed,
    long SizeBytes,
    string? IconUrl);

public sealed record BackupInfo(string File, string World, DateTimeOffset CreatedAt, long SizeBytes);

/// <summary>Welten (<c>saves/*/level.dat</c>) und deren Backups in <c>backups/</c>.</summary>
public sealed class WorldManager(AppPaths paths)
{
    private string SavesDir(string id) => Path.Combine(paths.GameDir(Text.RequireId(id)), "saves");

    public Task<IReadOnlyList<WorldInfo>> ListAsync(string instanceId, CancellationToken ct = default) => Task.Run(() =>
    {
        var saves = SavesDir(instanceId);
        if (!Directory.Exists(saves))
            return (IReadOnlyList<WorldInfo>)[];
        return Directory.EnumerateDirectories(saves)
            .Where(d => File.Exists(Path.Combine(d, "level.dat")))
            .Select(d => Read(instanceId, d))
            .OrderByDescending(w => w.LastPlayed ?? DateTimeOffset.MinValue)
            .ToList();
    }, ct);

    public static int Count(string savesDir) =>
        Directory.Exists(savesDir) ? Directory.EnumerateDirectories(savesDir).Count(d => File.Exists(Path.Combine(d, "level.dat"))) : 0;

    private static WorldInfo Read(string instanceId, string dir)
    {
        var folder = Path.GetFileName(dir);
        string name = folder, mode = "survival";
        string? version = null;
        bool hardcore = false, cheats = false;
        DateTimeOffset? lastPlayed = null;
        try
        {
            if (Nbt.ReadFile(Path.Combine(dir, "level.dat")).Get<NbtCompound>("Data") is { } data)
            {
                name = data.Get<string>("LevelName") is { Length: > 0 } n ? Text.Clean(n, 80) : folder;
                if (data.TryGetValue("LastPlayed", out var lp) && lp is long ms and > 0)
                    lastPlayed = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                mode = data.TryGetValue("GameType", out var gt) && gt is int type
                    ? type switch { 1 => "creative", 2 => "adventure", 3 => "spectator", _ => "survival" }
                    : "survival";
                hardcore = data.TryGetValue("hardcore", out var hc) && hc is sbyte h && h != 0;
                cheats = data.TryGetValue("allowCommands", out var ac) && ac is sbyte c && c != 0;
                version = data.Get<NbtCompound>("Version")?.Get<string>("Name");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            AppLog.Warn($"level.dat von {folder} nicht lesbar", ex);
        }
        var hasIcon = File.Exists(Path.Combine(dir, "icon.png"));
        return new WorldInfo(folder, name, mode, hardcore, cheats, version, lastPlayed, DirectorySize(dir),
            hasIcon ? $"axo://world-icon/{instanceId}/{Uri.EscapeDataString(folder)}" : null);
    }

    public string? IconPath(string instanceId, string folder)
    {
        if (!Text.IsValidId(instanceId))
            return null;
        var dir = PathGuard.SafeCombine(SavesDir(instanceId), folder);
        var icon = dir == null ? null : Path.Combine(dir, "icon.png");
        return icon != null && File.Exists(icon) ? icon : null;
    }

    public string WorldDir(string instanceId, string folder) =>
        PathGuard.SafeCombine(SavesDir(instanceId), folder) is { } dir && Directory.Exists(dir)
            ? dir
            : throw new AxoException("worlds.notFound");

    /// <summary>Welt als ZIP in <c>backups/</c> sichern.</summary>
    public async Task<BackupInfo> BackupAsync(string instanceId, string folder, JobContext job)
    {
        var dir = WorldDir(instanceId, folder);
        var backups = paths.InstanceBackups(instanceId);
        Directory.CreateDirectory(backups);
        var file = $"{folder}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
        var target = Path.Combine(backups, file);
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("session.lock", StringComparison.OrdinalIgnoreCase)).ToList();
        await using (var stream = File.Create(target + ".tmp"))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            for (var i = 0; i < files.Count; i++)
            {
                job.Token.ThrowIfCancellationRequested();
                var entry = zip.CreateEntry(folder + "/" + Path.GetRelativePath(dir, files[i]).Replace('\\', '/'),
                    CompressionLevel.Fastest);
                await using var source = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await using var target2 = entry.Open();
                await source.CopyToAsync(target2, job.Token);
                job.Report((double)(i + 1) / files.Count);
            }
        }
        File.Move(target + ".tmp", target, overwrite: true);
        return new BackupInfo(file, folder, DateTimeOffset.Now, new FileInfo(target).Length);
    }

    public IReadOnlyList<BackupInfo> ListBackups(string instanceId)
    {
        var dir = paths.InstanceBackups(Text.RequireId(instanceId));
        if (!Directory.Exists(dir))
            return [];
        return new DirectoryInfo(dir).EnumerateFiles("*.zip")
            .Select(f => new BackupInfo(f.Name, f.Name.Contains('-') ? f.Name[..f.Name.LastIndexOf('-', f.Name.LastIndexOf('_'))] : f.Name,
                f.CreationTime, f.Length))
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    /// <summary>Backup wiederherstellen. Gibt es die Welt schon, landet sie unter neuem Namen daneben.</summary>
    public async Task<string> RestoreBackupAsync(string instanceId, string backupFile, JobContext job)
    {
        var zipPath = PathGuard.Combine(paths.InstanceBackups(Text.RequireId(instanceId)), backupFile);
        var saves = SavesDir(instanceId);
        Directory.CreateDirectory(saves);
        using var zip = ZipFile.OpenRead(zipPath);
        var root = zip.Entries.Select(e => e.FullName.Split('/')[0]).FirstOrDefault() ?? "Welt";
        var target = root;
        for (var i = 2; Directory.Exists(Path.Combine(saves, target)); i++)
            target = $"{root} ({i})";
        var count = zip.Entries.Count;
        var done = 0;
        foreach (var entry in zip.Entries)
        {
            job.Token.ThrowIfCancellationRequested();
            var relative = target + entry.FullName[root.Length..];
            var path = PathGuard.Combine(saves, relative.TrimEnd('/'));
            if (entry.FullName.EndsWith('/'))
                Directory.CreateDirectory(path);
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

    public async Task DeleteAsync(string instanceId, string folder, CancellationToken ct = default)
    {
        var dir = WorldDir(instanceId, folder);
        await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin), ct);
    }

    public static long DirectorySize(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true, IgnoreInaccessible = true
                }).Sum(f => f.Length)
                : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
