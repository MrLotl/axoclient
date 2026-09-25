using Microsoft.Win32;

namespace AxoClient.Platform;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = AppInfo.Name;
    private const string Argument = "--autostart";

    public static bool IsAvailable => Environment.ProcessPath is { } path &&
                                      path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                      !path.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase);

    public static bool StartedByWindows =>
        Environment.GetCommandLineArgs().Any(a => a.Equals(Argument, StringComparison.OrdinalIgnoreCase));

    private static string Command => $"\"{Environment.ProcessPath}\" {Argument}";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string { Length: > 0 };
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Autostart abfragen", ex);
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void Refresh()
    {
        if (!IsAvailable)
            return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string { Length: > 0 } current && current != Command)
                key.SetValue(ValueName, Command);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Autostart-Eintrag aktualisieren", ex);
        }
    }
}
