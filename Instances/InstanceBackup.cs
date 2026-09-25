using System.IO.Compression;
using System.Text.Json;

namespace AxoClient.Instances;

[Flags]
public enum BackupParts
{
    None = 0,
    Worlds = 1,
    Content = 2,
    Settings = 4,
    All = Worlds | Content | Settings
}

public class InstanceBackupInfo
{
    public required string FilePath { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required long SizeBytes { get; init; }
    public BackupParts Parts { get; init; }
    public string Note { get; init; } = "";
    public string MinecraftVersion { get; init; } = "";
    public LoaderType Loader { get; init; }

    public string FileName => Path.GetFileName(FilePath);
    public string CreatedText => CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string Title => Note.Length > 0 ? Note : "Sicherung vom " + CreatedText;

    public string Details => string.Join("  ·  ", new[]
    {
        CreatedText,
        InstanceBackup.PartsText(Parts),
        Formats.Size(SizeBytes),
        MinecraftVersion.Length > 0 ? $"{Loader} {MinecraftVersion}" : null
    }.Where(s => !string.IsNullOrEmpty(s)));
}

public static class InstanceBackup
{
    private const string ManifestName = "axoclient-backup.json";
    private const string AsideSuffix = ".vor-backup";

    private static readonly BackupParts[] SingleParts = [BackupParts.Worlds, BackupParts.Content, BackupParts.Settings];

    private sealed class Manifest
    {
        public string Instance { get; set; } = "";
        public string Minecraft { get; set; } = "";
        public LoaderType Loader { get; set; }
        public BackupParts Parts { get; set; }
        public string Note { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
    }

    public static bool IsBackupArchive(IEnumerable<string> entryNames) =>
        entryNames.Any(n => n.Equals(ManifestName, StringComparison.OrdinalIgnoreCase));

    public static string PartsText(BackupParts parts)
    {
        var names = new List<string>();
        if (parts.HasFlag(BackupParts.Worlds))
            names.Add("Welten");
        if (parts.HasFlag(BackupParts.Content))
            names.Add("Mods & Pakete");
        if (parts.HasFlag(BackupParts.Settings))
            names.Add("Einstellungen");
        return names.Count > 0 ? string.Join(", ", names) : "nichts";
    }

    private static IEnumerable<string> EntriesOf(BackupParts parts) => SingleParts
        .Where(p => parts.HasFlag(p))
        .SelectMany(p => p switch
        {
            BackupParts.Worlds => new[] { "saves" },
            BackupParts.Content => new[] { "mods", "resourcepacks", "texturepacks", "shaderpacks", "launcher-content.json" },
            _ => new[]
            {
                "config", "options.txt", "optionsof.txt", "optionsshaders.txt", "servers.dat",
                "launcher-packprofiles.json", "keybinds.txt"
            }
        });

    public static Task<List<InstanceBackupInfo>> ListAsync(Installation inst) => Task.Run(() =>
        Directory.Exists(inst.BackupDir)
            ? Directory.GetFiles(inst.BackupDir, "instanz-*.zip")
                .Select(Read)
                .OfType<InstanceBackupInfo>()
                .OrderByDescending(b => b.CreatedUtc)
                .ToList()
            : []);

    private static InstanceBackupInfo? Read(string path)
    {
        var file = new FileInfo(path);
        Manifest? manifest = null;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry(ManifestName) is { } entry)
            {
                using var stream = entry.Open();
                manifest = JsonSerializer.Deserialize<Manifest>(stream, JsonFiles.Indented);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            return null;
        }

        return new InstanceBackupInfo
        {
            FilePath = path,
            CreatedUtc = manifest?.CreatedUtc ?? file.LastWriteTimeUtc,
            SizeBytes = file.Length,
            Parts = manifest?.Parts ?? BackupParts.All,
            Note = manifest?.Note ?? "",
            MinecraftVersion = manifest?.Minecraft ?? "",
            Loader = manifest?.Loader ?? LoaderType.Vanilla
        };
    }

    public static Task<string> CreateAsync(Installation inst, BackupParts parts, string note, WorkProgress progress) =>
        Task.Run(() =>
        {
            if (parts == BackupParts.None)
                throw new InvalidOperationException("Es wurde nichts zum Sichern ausgewählt.");

            Directory.CreateDirectory(inst.BackupDir);
            var target = Path.Combine(inst.BackupDir, $"instanz-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip");

            progress.Text.Report("Suche Dateien...");
            var files = CollectFiles(inst, parts);
            if (files.Count == 0)
                throw new InvalidOperationException("In dieser Instanz gibt es zu den gewählten Teilen noch nichts zu sichern.");

            try
            {
                using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
                using (var stream = zip.CreateEntry(ManifestName).Open())
                    JsonSerializer.Serialize(stream, new Manifest
                    {
                        Instance = inst.Name,
                        Minecraft = inst.MinecraftVersion,
                        Loader = inst.Loader,
                        Parts = parts,
                        Note = note,
                        CreatedUtc = DateTime.UtcNow
                    }, JsonFiles.Indented);

                var done = 0;
                foreach (var (full, relative) in files)
                {
                    progress.Cancel.ThrowIfCancellationRequested();
                    try
                    {
                        zip.CreateEntryFromFile(full, relative, CompressionLevel.Optimal);
                    }
                    catch (IOException ex)
                    {
                        ErrorReport.Log("Datei \"" + relative + "\" sichern", ex);
                    }
                    ReportStep(progress, ++done, files.Count, "Sichere");
                }
                return target;
            }
            catch
            {
                FileOps.TryDelete(target);
                throw;
            }
        }, progress.Cancel);

    private static List<(string Full, string Relative)> CollectFiles(Installation inst, BackupParts parts)
    {
        var files = new List<(string, string)>();
        foreach (var name in EntriesOf(parts))
        {
            var path = inst.GameFile(name);
            if (File.Exists(path))
                files.Add((path, name));
            else if (Directory.Exists(path))
                foreach (var file in FileOps.EnumerateFilesSafe(path, "*"))
                    files.Add((file, Path.GetRelativePath(inst.GameDir, file).Replace('\\', '/')));
        }
        return files;
    }

    public static Task<List<string>> RestoreAsync(Installation inst, InstanceBackupInfo backup, BackupParts parts,
        WorkProgress progress) => Task.Run(() =>
    {
        var report = new List<string>();
        using var zip = ZipFile.OpenRead(backup.FilePath);

        var wanted = parts & backup.Parts;
        if (wanted == BackupParts.None)
            throw new InvalidOperationException("Diese Sicherung enthält die gewählten Teile nicht.");
        var prefixes = EntriesOf(wanted).ToList();

        progress.Text.Report("Lege bisherige Dateien zur Seite...");
        foreach (var name in prefixes)
        {
            var path = inst.GameFile(name);
            if (!Directory.Exists(path) && !File.Exists(path))
                continue;
            var aside = path + AsideSuffix;
            FileOps.TryDelete(aside);
            try
            {
                if (Directory.Exists(path))
                    Directory.Move(path, aside);
                else
                    File.Move(path, aside, overwrite: true);
                report.Add($"\"{name}\" wurde zu \"{Path.GetFileName(aside)}\" umbenannt.");
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(
                    $"\"{name}\" konnte nicht zur Seite gelegt werden ({ErrorReport.Short(ex)}). " +
                    "Läuft das Spiel noch? Dann zuerst schließen.");
            }
        }

        var entries = zip.Entries.Where(e => e.Name.Length > 0 && Matches(e.FullName, prefixes)).ToList();
        var done = 0;
        foreach (var entry in entries)
        {
            progress.Cancel.ThrowIfCancellationRequested();
            if (FileOps.ResolveInside(inst.GameDir, entry.FullName) is not { } target)
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            ReportStep(progress, ++done, entries.Count, "Stelle wieder her");
        }
        report.Add($"{done} Datei(en) aus der Sicherung vom {backup.CreatedText} wiederhergestellt.");
        return report;
    }, progress.Cancel);

    public static int CleanUpAside(Installation inst) =>
        EntriesOf(BackupParts.All).Count(name => FileOps.TryDelete(inst.GameFile(name) + AsideSuffix));

    public static bool HasAside(Installation inst) =>
        EntriesOf(BackupParts.All).Any(name =>
        {
            var path = inst.GameFile(name) + AsideSuffix;
            return Directory.Exists(path) || File.Exists(path);
        });

    private static void ReportStep(WorkProgress progress, int done, int total, string verb)
    {
        if (done % 25 != 0 && done != total)
            return;
        progress.Text.Report($"{verb}... {done} von {total} Dateien");
        progress.Fraction.Report((double)done / Math.Max(total, 1));
    }

    private static bool Matches(string entryPath, IEnumerable<string> prefixes) =>
        prefixes.Any(p => entryPath.Equals(p, StringComparison.OrdinalIgnoreCase)
                          || entryPath.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));
}
