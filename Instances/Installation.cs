using System.Text.Json.Serialization;

namespace AxoClient.Instances;

public enum LoaderType
{
    Vanilla,
    Fabric,
    Forge
}

public static class LoaderTypes
{
    public static readonly LoaderType[] All = [LoaderType.Vanilla, LoaderType.Fabric, LoaderType.Forge];

    public static string ModrinthName(this LoaderType loader) => loader == LoaderType.Forge ? "forge" : "fabric";
}

public class Installation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public LoaderType Loader { get; set; } = LoaderType.Vanilla;
    public string MinecraftVersion { get; set; } = LauncherSettings.DefaultMinecraftVersion;
    public string? LoaderVersion { get; set; }
    public string GameDir { get; set; } = "";

    public int? MaxRamMb { get; set; }
    public string? JavaPath { get; set; }
    public string? JvmPreset { get; set; }
    public string? JvmArguments { get; set; }

    public long PlayTimeSeconds { get; set; }
    public int LaunchCount { get; set; }
    public DateTime? LastPlayedUtc { get; set; }
    public int CrashCount { get; set; }
    public DateTime? LastCrashUtc { get; set; }
    public Dictionary<string, long> PlayDays { get; set; } = [];

    public string? IconFile { get; set; }

    [JsonIgnore] public string Description => $"{Loader} · {MinecraftVersion}";
    [JsonIgnore] public string LoaderInitial => Loader.ToString()[..1];
    [JsonIgnore] public bool CanUseMods => Loader != LoaderType.Vanilla;

    [JsonIgnore] public string ModsDir => Path.Combine(GameDir, "mods");
    [JsonIgnore] public string SavesDir => Path.Combine(GameDir, "saves");
    [JsonIgnore] public string ConfigDir => Path.Combine(GameDir, "config");
    [JsonIgnore] public string LogsDir => Path.Combine(GameDir, "logs");
    [JsonIgnore] public string LatestLog => Path.Combine(LogsDir, "latest.log");
    [JsonIgnore] public string CrashReportsDir => Path.Combine(GameDir, "crash-reports");
    [JsonIgnore] public string ScreenshotsDir => Path.Combine(GameDir, "screenshots");
    [JsonIgnore] public string OptionsFile => Path.Combine(GameDir, "options.txt");
    [JsonIgnore] public string BackupDir => Path.Combine(AppPaths.Backups, Path.GetFileName(GameDir.TrimEnd('\\', '/')));

    public string ContentDir(ContentType type) => Path.Combine(GameDir, ContentTypes.Folder(type, MinecraftVersion));

    public string GameFile(string name) => Path.Combine(GameDir, name);
}
