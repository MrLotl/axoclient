using System.Diagnostics;
using System.Runtime.InteropServices;
using AxoClient.Core.Common;
using AxoClient.Core.Updates;
using Microsoft.Win32;

namespace AxoClient.App;

/// <summary>
/// Die <c>AxoClient-Setup.exe</c> ist der Launcher selbst. Startet man sie irgendwo außerhalb des
/// Installationsordners, kopiert sie sich nach <c>%LocalAppData%\Programs\AxoClient\AxoClient.exe</c>, legt
/// Verknüpfungen (Startmenü, Desktop) und den Eintrag unter "Apps" an und startet den Launcher. Ein zweites Mal
/// ausgeführt überschreibt sie einfach (Reparatur/Update). Keine Admin-Rechte, kein Zusatzwerkzeug.
/// </summary>
internal static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AxoClient";
    private static readonly string Name = $"AxoClient-{Environment.UserName}-{Environment.UserDomainName}".Replace('\\', '_');

    /// <summary>Erledigt Setup-, Deinstallations- und Update-Aufgaben; <c>true</c> heißt: Programm beenden.</summary>
    public static bool Handle(string[] args, out int exitCode)
    {
        exitCode = 0;
        try
        {
            if (args.Contains("--uninstall"))
            {
                exitCode = Uninstall();
                return true;
            }
            if (InstallLocation.IsSingleFile && !InstallLocation.IsInstalled)
            {
                exitCode = Install();
                return true;
            }
            WaitForPreviousVersion(args);
            if (InstallLocation.IsInstalled)
            {
                InstallLocation.TryDeleteOld();
                RegisterUninstall();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException)
        {
            AppLog.Warn("Installation", ex);
            if (args.Contains("--uninstall") || (InstallLocation.IsSingleFile && !InstallLocation.IsInstalled))
            {
                NativeMessage.Show("AxoClient konnte nicht installiert werden", ex.Message);
                exitCode = 1;
                return true;
            }
        }
        return false;
    }

    private static int Install()
    {
        while (LauncherRunning())
        {
            if (!NativeMessage.RetryCancel("AxoClient-Setup",
                    "AxoClient läuft noch. Schließe AxoClient (auch unten rechts im Infobereich) und klicke auf „Wiederholen“."))
                return 1;
        }

        InstallLocation.ReplaceExe(Environment.ProcessPath!, copy: true);
        var exe = InstallLocation.ExePath;
        CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "AxoClient.lnk"), exe);
        CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AxoClient.lnk"), exe);
        RegisterUninstall();
        RemoveOldVelopackInstall();
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = InstallLocation.Directory });
        return 0;
    }

    private static int Uninstall()
    {
        if (LauncherRunning())
        {
            NativeMessage.Show("AxoClient deinstallieren", "AxoClient läuft noch. Bitte zuerst schließen.");
            return 1;
        }
        if (!NativeMessage.YesNo("AxoClient deinstallieren",
                "AxoClient wirklich entfernen?\n\nDeine Instanzen, Welten und Einstellungen bleiben in %AppData%\\AxoClient erhalten."))
            return 0;

        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "AxoClient.lnk"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AxoClient.lnk"));
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
            run?.DeleteValue("AxoClient", throwOnMissingValue: false);

        // Der Ordner mit der laufenden EXE lässt sich erst löschen, wenn sie beendet ist
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{InstallLocation.Directory}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        });
        return 0;
    }

    /// <summary>Nach einem Update startet die neue Version mit <c>--after &lt;pid&gt;</c> und wartet auf die alte.</summary>
    private static void WaitForPreviousVersion(string[] args)
    {
        var i = Array.IndexOf(args, "--after");
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid))
            return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException)
        {
            // Schon beendet
        }
    }

    private static bool LauncherRunning()
    {
        if (!Mutex.TryOpenExisting($"Local\\{Name}", out var mutex))
            return false;
        mutex.Dispose();
        return true;
    }

    /// <summary>Eintrag unter "Apps" bzw. "Programme und Features" (pro Benutzer).</summary>
    private static void RegisterUninstall()
    {
        var exe = InstallLocation.ExePath;
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        if (key.GetValue("DisplayVersion") as string == AppInfo.Version && key.GetValue("InstallLocation") as string == InstallLocation.Directory)
            return;
        key.SetValue("DisplayName", "AxoClient");
        key.SetValue("DisplayVersion", AppInfo.Version);
        key.SetValue("Publisher", "AxoClient");
        key.SetValue("DisplayIcon", $"\"{exe}\",0");
        key.SetValue("InstallLocation", InstallLocation.Directory);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("URLInfoAbout", AppInfo.Repository);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        if (File.Exists(exe))
            key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
    }

    /// <summary>.lnk über das Windows-Skripting-Objekt (WScript.Shell), keine zusätzliche Bibliothek nötig.</summary>
    private static void CreateShortcut(string path, string target)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null)
            return;
        object? shell = null;
        object? link = null;
        try
        {
            shell = Activator.CreateInstance(type);
            link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [path]);
            if (link == null)
                return;
            var linkType = link.GetType();
            void Set(string name, object value) =>
                linkType.InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, link, [value]);
            Set("TargetPath", target);
            Set("WorkingDirectory", Path.GetDirectoryName(target)!);
            Set("IconLocation", target + ",0");
            Set("Description", "AxoClient");
            linkType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
        }
        catch (Exception ex) when (ex is COMException or System.Reflection.TargetInvocationException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Verknüpfung {path}", ex);
        }
        finally
        {
            if (link != null && Marshal.IsComObject(link))
                Marshal.FinalReleaseComObject(link);
            if (shell != null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>
    /// Frühere Installationen über Velopack lagen in <c>%LocalAppData%\AxoClient</c> (nur Programmdateien, die Daten
    /// liegen in %AppData%). Verknüpfungen und der Apps-Eintrag wurden oben schon überschrieben.
    /// </summary>
    private static void RemoveOldVelopackInstall()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AxoClient");
        if (!Directory.Exists(dir))
            return;
        if (!File.Exists(Path.Combine(dir, "Update.exe")) && !Directory.Exists(Path.Combine(dir, "current"))
                                                           && !Directory.Exists(Path.Combine(dir, "packages")))
            return;
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Alte Velopack-Installation entfernen", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"{path} löschen", ex);
        }
    }
}
