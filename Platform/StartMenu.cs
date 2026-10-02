namespace AxoClient.Platform;

public static class StartMenu
{
    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");

    public static void EnsureShortcut()
    {
        if (!Autostart.IsAvailable || AppInfo.Version == AppInfo.DevVersion || Environment.ProcessPath is not { } exe)
            return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(ShortcutPath);
                if (File.Exists(ShortcutPath) && string.Equals((string)shortcut.TargetPath, exe, StringComparison.OrdinalIgnoreCase))
                    return;
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
                shortcut.IconLocation = exe + ",0";
                shortcut.Description = "Minecraft-Launcher";
                shortcut.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Startmenü-Verknüpfung anlegen", ex);
        }
    }
}
