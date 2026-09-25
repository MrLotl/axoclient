using System.IO.Compression;
using System.Windows.Media.Imaging;

namespace AxoClient.Instances;

public class WorldInfo
{
    public required string FolderName { get; init; }
    public required string FullPath { get; init; }
    public required string DisplayName { get; init; }
    public DateTime? LastPlayed { get; init; }
    public string GameMode { get; init; } = "";
    public string? VersionName { get; init; }
    public long SizeBytes { get; init; }
    public BitmapSource? Icon { get; init; }

    public string Details => string.Join(" · ", new[]
    {
        GameMode,
        VersionName,
        LastPlayed is { } t ? $"zuletzt {t:dd.MM.yyyy HH:mm}" : null,
        Formats.Size(SizeBytes)
    }.Where(s => !string.IsNullOrEmpty(s)));
}

public class WorldStore(Installation inst)
{
    public string SavesDir => inst.SavesDir;

    public static int Count(string savesDir) =>
        Directory.Exists(savesDir)
            ? Directory.GetDirectories(savesDir).Count(d => File.Exists(Path.Combine(d, "level.dat")))
            : 0;

    public Task<List<WorldInfo>> LoadAsync() => Task.Run(() =>
    {
        if (!Directory.Exists(SavesDir))
            return new List<WorldInfo>();
        return Directory.GetDirectories(SavesDir)
            .Where(dir => File.Exists(Path.Combine(dir, "level.dat")))
            .Select(ReadWorld)
            .OrderByDescending(w => w.LastPlayed ?? DateTime.MinValue)
            .ToList();
    });

    private static WorldInfo ReadWorld(string dir)
    {
        var folder = Path.GetFileName(dir);
        string name = folder, mode = "", version = "";
        DateTime? lastPlayed = null;
        try
        {
            if (Nbt.ReadFile(Path.Combine(dir, "level.dat")).Get<NbtCompound>("Data") is { } data)
            {
                name = data.Get<string>("LevelName") is { Length: > 0 } n ? n : folder;
                if (data.TryGetValue("LastPlayed", out var lp) && lp is long ms && ms > 0)
                    lastPlayed = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
                var hardcore = data.TryGetValue("hardcore", out var hc) && hc is sbyte b && b != 0;
                mode = hardcore ? "Hardcore" : data.Get<int>("GameType") switch
                {
                    1 => "Kreativ",
                    2 => "Abenteuer",
                    3 => "Zuschauer",
                    _ => "Überleben"
                };
                version = data.Get<NbtCompound>("Version")?.Get<string>("Name") ?? "";
            }
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"level.dat der Welt \"{folder}\" lesen", ex);
        }

        var icon = Path.Combine(dir, "icon.png");
        return new WorldInfo
        {
            FolderName = folder,
            FullPath = dir,
            DisplayName = name,
            LastPlayed = lastPlayed ?? Directory.GetLastWriteTime(dir),
            GameMode = mode,
            VersionName = version,
            SizeBytes = FileOps.DirectorySize(dir),
            Icon = File.Exists(icon) ? Images.FromFile(icon) : null
        };
    }

    public Task<string> BackupAsync(WorldInfo world) => Task.Run(() =>
    {
        Directory.CreateDirectory(inst.BackupDir);
        var zip = Path.Combine(inst.BackupDir, $"{world.FolderName}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");
        ZipFile.CreateFromDirectory(world.FullPath, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
        return zip;
    });

    public Task<string> ImportAsync(string path) => Task.Run(() =>
    {
        Directory.CreateDirectory(SavesDir);
        if (Directory.Exists(path))
        {
            if (!File.Exists(Path.Combine(path, "level.dat")))
                throw new InvalidOperationException("Der Ordner enthält keine Minecraft-Welt (level.dat fehlt).");
            return CopyIn(path, Path.GetFileName(path.TrimEnd('\\', '/')));
        }

        var temp = Path.Combine(Path.GetTempPath(), "axoclient-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(path, temp);
            var levelDat = Directory.GetFiles(temp, "level.dat", System.IO.SearchOption.AllDirectories)
                               .OrderBy(p => p.Length).FirstOrDefault()
                           ?? throw new InvalidOperationException(
                               "Das Archiv enthält keine Minecraft-Welt (level.dat fehlt).");
            var worldDir = Path.GetDirectoryName(levelDat)!;
            return CopyIn(worldDir, worldDir == temp ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(worldDir));
        }
        finally
        {
            FileOps.TryDelete(temp);
        }
    });

    public static int CopyAll(string fromSaves, string toSaves)
    {
        if (!Directory.Exists(fromSaves))
            return 0;
        Directory.CreateDirectory(toSaves);
        var count = 0;
        foreach (var world in Directory.GetDirectories(fromSaves).Where(d => File.Exists(Path.Combine(d, "level.dat"))))
        {
            var target = FileOps.UniqueDirectory(toSaves, Path.GetFileName(world));
            FileOps.CopyDirectory(world, target, overwrite: false);
            File.Delete(Path.Combine(target, "session.lock"));
            count++;
        }
        return count;
    }

    private string CopyIn(string worldDir, string name)
    {
        var target = FileOps.UniqueDirectory(SavesDir, name);
        FileOps.CopyDirectory(worldDir, target, overwrite: false);
        return Path.GetFileName(target);
    }
}
