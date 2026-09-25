namespace AxoClient.Core;

public static class AppPaths
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static readonly string OldLauncherDir = Path.Combine(AppData, ".mclauncher");

    public static readonly string LauncherDir = ResolveLauncherDir();

    private static string DefaultLauncherDir => Path.Combine(AppData, ".axoclient");

    public static string OfficialMinecraftDir => Path.Combine(AppData, ".minecraft");

    public static string SettingsFile => Path.Combine(LauncherDir, "launcher-settings.json");
    public static string Instances => Path.Combine(LauncherDir, "instances");
    public static string Icons => Path.Combine(LauncherDir, "icons");
    public static string Skins => Path.Combine(LauncherDir, "skins");
    public static string Backups => Path.Combine(LauncherDir, "backups");
    public static string Libraries => Path.Combine(LauncherDir, "libraries");
    public static string Versions => Path.Combine(LauncherDir, "versions");
    public static string Resources => Path.Combine(LauncherDir, "resources");
    public static string Assets => Path.Combine(LauncherDir, "assets");
    public static string Runtime => Path.Combine(LauncherDir, "runtime");

    public static string ErrorLog => Path.Combine(
        string.IsNullOrEmpty(LauncherDir) ? DefaultLauncherDir : LauncherDir, "logs", "fehler.log");

    private static string ResolveLauncherDir()
    {
        var dir = DefaultLauncherDir;
        if (Directory.Exists(dir) || !Directory.Exists(OldLauncherDir))
            return dir;
        try
        {
            Directory.Move(OldLauncherDir, dir);
            return dir;
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"Ordner \"{OldLauncherDir}\" konnte nicht nach \"{dir}\" umbenannt werden", ex);
            return OldLauncherDir;
        }
    }
}
