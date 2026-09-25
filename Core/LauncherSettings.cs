using System.Text.Json;
using System.Text.Json.Serialization;

namespace AxoClient.Core;

public enum AfterLaunchAction
{
    KeepOpen,
    Minimize,
    Hide,
    Close
}

public class LauncherSettings
{
    public const string DefaultMinecraftVersion = "26.2";

    public List<Installation> Installations { get; set; } = [];
    public string? SelectedInstallationId { get; set; }

    public bool ShowSnapshots { get; set; }
    public bool AxoVersionsOnly { get; set; }
    public bool ShowOldVersions { get; set; }
    public bool InstancesAsTiles { get; set; }
    public bool ContentAsTiles { get; set; }

    public bool BadgeEnabled { get; set; } = true;
    public string? BadgeToken { get; set; }
    public string? BadgeTokenUuid { get; set; }
    public bool DiscordEnabled { get; set; } = true;

    public int MaxRamMb { get; set; } = 4096;
    public string JvmPreset { get; set; } = JvmPresets.CustomId;
    public string JvmArguments { get; set; } = "";
    public string? JavaPath { get; set; }
    public bool PreLaunchCheck { get; set; } = true;
    public int GameWidth { get; set; }
    public int GameHeight { get; set; }
    public bool FullScreen { get; set; }
    public bool MaximizeOnLaunch { get; set; }
    public AfterLaunchAction AfterLaunch { get; set; } = AfterLaunchAction.Minimize;
    public bool AutostartMinimized { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MinimizeOnLaunch { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    public static (string Text, Exception Error)? LoadProblem { get; private set; }

    public static LauncherSettings Load()
    {
        var path = AppPaths.SettingsFile;
        LauncherSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), JsonFiles.Indented)
                       ?? new LauncherSettings();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            settings = new LauncherSettings();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Einstellungen laden", ex);
            settings = new LauncherSettings();
            var saved = KeepBrokenFile(path);
            LoadProblem = ($"Die gespeicherten Einstellungen ({path}) konnten nicht gelesen werden. " +
                           "AxoClient startet deshalb mit den Standardwerten; deine Instanzordner sind noch da." +
                           (saved != null ? $"\n\nDie bisherige Datei liegt jetzt hier: {saved}" : ""), ex);
        }

        settings.Migrate();
        return settings;
    }

    public void Save() => JsonFiles.Write(AppPaths.SettingsFile, this);

    private void Migrate()
    {
        if (Installations.Count == 0)
        {
            Installations.Add(new Installation
            {
                Name = "Vanilla",
                Loader = LoaderType.Vanilla,
                MinecraftVersion = Version ?? DefaultMinecraftVersion,
                GameDir = AppPaths.LauncherDir
            });
        }
        Version = null;
        if (MinimizeOnLaunch == false)
            AfterLaunch = AfterLaunchAction.KeepOpen;
        MinimizeOnLaunch = null;

        if (MoveGameDirsOutOfOldLauncherDir())
            Save();
    }

    private bool MoveGameDirsOutOfOldLauncherDir()
    {
        var oldDir = AppPaths.OldLauncherDir;
        if (string.Equals(AppPaths.LauncherDir, oldDir, StringComparison.OrdinalIgnoreCase))
            return false;
        var changed = false;
        foreach (var inst in Installations)
        {
            if (!inst.GameDir.StartsWith(oldDir, StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = inst.GameDir[oldDir.Length..];
            if (rest.Length > 0 && rest[0] is not ('\\' or '/'))
                continue;
            inst.GameDir = AppPaths.LauncherDir + rest;
            changed = true;
        }
        return changed;
    }

    private static string? KeepBrokenFile(string path)
    {
        try
        {
            var broken = path + ".kaputt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(path, broken, overwrite: true);
            return broken;
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Kaputte Einstellungen zur Seite legen", ex);
            return null;
        }
    }
}
