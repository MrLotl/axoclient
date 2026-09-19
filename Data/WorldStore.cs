using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;
using Microsoft.VisualBasic.FileIO;

namespace McLauncher;

/// <summary>Eine Einzelspielerwelt im saves-Ordner einer Instanz.</summary>
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
        FormatSize(SizeBytes)
    }.Where(s => !string.IsNullOrEmpty(s)));

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        _ => $"{bytes / 1024.0:0} KB"
    };
}

/// <summary>Liest und verwaltet die Welten (saves) einer Instanz.</summary>
public class WorldStore(Installation inst)
{
    public string SavesDir => Path.Combine(inst.GameDir, "saves");

    public static string BackupDir(Installation inst) =>
        Path.Combine(AppState.LauncherDir, "backups", Path.GetFileName(inst.GameDir.TrimEnd('\\', '/')));

    /// <summary>Lädt alle Welten (im Hintergrund, da Ordnergrößen berechnet werden).</summary>
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
            var data = Nbt.ReadFile(Path.Combine(dir, "level.dat")).Get<NbtCompound>("Data");
            if (data != null)
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
        catch
        {
            // Beschädigte level.dat: Ordnername anzeigen
        }

        return new WorldInfo
        {
            FolderName = folder,
            FullPath = dir,
            DisplayName = name,
            LastPlayed = lastPlayed ?? Directory.GetLastWriteTime(dir),
            GameMode = mode,
            VersionName = version,
            SizeBytes = DirectorySize(dir),
            Icon = LoadIcon(Path.Combine(dir, "icon.png"))
        };
    }

    private static BitmapSource? LoadIcon(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // Datei nicht gesperrt lassen
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public static long DirectorySize(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", System.IO.SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Sichert die Welt als ZIP im Backup-Ordner der Instanz und gibt den Pfad zurück.</summary>
    public Task<string> BackupAsync(WorldInfo world) => Task.Run(() =>
    {
        var dir = BackupDir(inst);
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, $"{world.FolderName}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");
        ZipFile.CreateFromDirectory(world.FullPath, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
        return zip;
    });

    /// <summary>Verschiebt die Welt in den Papierkorb (nicht endgültig gelöscht).</summary>
    public static void MoveToRecycleBin(WorldInfo world) =>
        FileSystem.DeleteDirectory(world.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

    /// <summary>Importiert eine Welt aus einer ZIP-Datei oder einem Ordner (mit level.dat).</summary>
    public Task<string> ImportAsync(string path) => Task.Run(() =>
    {
        Directory.CreateDirectory(SavesDir);
        if (Directory.Exists(path))
        {
            if (!File.Exists(Path.Combine(path, "level.dat")))
                throw new InvalidOperationException("Der Ordner enthält keine Minecraft-Welt (level.dat fehlt).");
            var target = UniqueDir(SavesDir, Path.GetFileName(path.TrimEnd('\\', '/')));
            CopyDirectory(path, target, overwrite: false);
            return Path.GetFileName(target);
        }

        // ZIP: Welt kann direkt im Archiv oder in einem Unterordner liegen
        var temp = Path.Combine(Path.GetTempPath(), "mclauncher-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(path, temp);
            var levelDat = Directory.GetFiles(temp, "level.dat", System.IO.SearchOption.AllDirectories)
                .OrderBy(p => p.Length).FirstOrDefault()
                ?? throw new InvalidOperationException("Das Archiv enthält keine Minecraft-Welt (level.dat fehlt).");
            var worldDir = Path.GetDirectoryName(levelDat)!;
            var name = worldDir == temp ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(worldDir);
            var target = UniqueDir(SavesDir, name);
            CopyDirectory(worldDir, target, overwrite: false);
            return Path.GetFileName(target);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* temporärer Ordner */ }
        }
    });

    /// <summary>Freier Ordnername: "Welt", "Welt (2)", "Welt (3)" ...</summary>
    public static string UniqueDir(string parent, string name)
    {
        var target = Path.Combine(parent, name);
        for (var i = 2; Directory.Exists(target) || File.Exists(target); i++)
            target = Path.Combine(parent, $"{name} ({i})");
        return target;
    }

    public static void CopyDirectory(string source, string target, bool overwrite)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            var dest = Path.Combine(target, Path.GetFileName(file));
            if (overwrite || !File.Exists(dest))
                File.Copy(file, dest, overwrite);
        }
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)), overwrite);
    }
}
