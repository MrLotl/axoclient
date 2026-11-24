using System.Diagnostics;
using System.Runtime.InteropServices;
using AxoClient.Common;
using AxoClient.Updates.Contract;
using Microsoft.Win32;

namespace AxoClient.App
{
    /// <summary>
    /// Die <c>AxoClient-Setup.exe</c> ist der Launcher selbst. Startet man sie irgendwo außerhalb des
    /// Installationsordners, kopiert sie sich nach <c>%LocalAppData%\Programs\AxoClient\AxoClient.exe</c>, legt
    /// Verknüpfungen (Startmenü, Desktop) und den Eintrag unter "Apps" an und startet den Launcher. Ein zweites Mal
    /// ausgeführt überschreibt sie einfach (Reparatur/Update). Keine Admin-Rechte, kein Zusatzwerkzeug.
    /// </summary>
    public class Installer
    {
        private readonly IAppLog _appLog;
        private readonly IAppInfo _appInfo;
        private readonly IInstallLocation _installLocation;
        private readonly NativeMessage _nativeMessage;
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AxoClient";
        private readonly string _name = $"AxoClient-{Environment.UserName}-{Environment.UserDomainName}".Replace('\\', '_');


        public Installer(IAppLog appLog, IAppInfo appInfo, IInstallLocation installLocation, NativeMessage nativeMessage)
        {
            _appLog = appLog;
            _appInfo = appInfo;
            _installLocation = installLocation;
            _nativeMessage = nativeMessage;
        }

        /// <summary>Erledigt Setup-, Deinstallations- und Update-Aufgaben; <c>true</c> heißt: Programm beenden.</summary>
        public bool Handle(string[] arguments, out int exitCode)
        {
            exitCode = 0;
            try
            {
                if (arguments.Contains("--uninstall"))
                {
                    exitCode = uninstall();
                    return true;
                }
                if (_installLocation.IsSingleFile && !_installLocation.IsInstalled)
                {
                    exitCode = install();
                    return true;
                }
                waitForPreviousVersion(arguments);
                if (_installLocation.IsInstalled)
                {
                    _installLocation.TryDeleteOld();
                    registerUninstall();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException)
            {
                _appLog.Warn("Installation", exception);
                if (arguments.Contains("--uninstall") || (_installLocation.IsSingleFile && !_installLocation.IsInstalled))
                {
                    _nativeMessage.Show("AxoClient konnte nicht installiert werden", exception.Message);
                    exitCode = 1;
                    return true;
                }
            }
            return false;
        }

        private int install()
        {
            while (launcherRunning())
            {
                if (!_nativeMessage.RetryCancel("AxoClient-Setup",
                        "AxoClient läuft noch. Schließe AxoClient (auch unten rechts im Infobereich) und klicke auf „Wiederholen“."))
                {
                    return 1;
                }
            }

            _installLocation.ReplaceExe(Environment.ProcessPath!, copy: true);
            var exePath = _installLocation.ExePath;
            createShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "AxoClient.lnk"), exePath);
            createShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AxoClient.lnk"), exePath);
            registerUninstall();
            removeOldVelopackInstall();
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false, WorkingDirectory = _installLocation.Directory });
            return 0;
        }

        private int uninstall()
        {
            if (launcherRunning())
            {
                _nativeMessage.Show("AxoClient deinstallieren", "AxoClient läuft noch. Bitte zuerst schließen.");
                return 1;
            }
            if (!_nativeMessage.YesNo("AxoClient deinstallieren",
                    "AxoClient wirklich entfernen?\n\nDeine Instanzen, Welten und Einstellungen bleiben in %AppData%\\AxoClient erhalten."))
            {
                return 0;
            }

            tryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "AxoClient.lnk"));
            tryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AxoClient.lnk"));
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
            {
                run?.DeleteValue("AxoClient", throwOnMissingValue: false);
            }

            // Der Ordner mit der laufenden EXE lässt sich erst löschen, wenn sie beendet ist
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{_installLocation.Directory}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            });
            return 0;
        }

        /// <summary>Nach einem Update startet die neue Version mit <c>--after &lt;pid&gt;</c> und wartet auf die alte.</summary>
        private void waitForPreviousVersion(string[] arguments)
        {
            var afterIndex = Array.IndexOf(arguments, "--after");
            if (afterIndex < 0 || afterIndex + 1 >= arguments.Length || !int.TryParse(arguments[afterIndex + 1], out var processId))
            {
                return;
            }

            try
            {
                using var previous = Process.GetProcessById(processId);
                previous.WaitForExit(TimeSpan.FromSeconds(30));
            }
            catch (ArgumentException)
            {
                // Schon beendet
            }
        }

        private bool launcherRunning()
        {
            if (!Mutex.TryOpenExisting($"Local\\{_name}", out var mutex))
            {
                return false;
            }

            mutex.Dispose();
            return true;
        }

        /// <summary>Eintrag unter "Apps" bzw. "Programme und Features" (pro Benutzer).</summary>
        private void registerUninstall()
        {
            var exePath = _installLocation.ExePath;
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            if (key.GetValue("DisplayVersion") as string == _appInfo.Version && key.GetValue("InstallLocation") as string == _installLocation.Directory)
            {
                return;
            }

            key.SetValue("DisplayName", "AxoClient");
            key.SetValue("DisplayVersion", _appInfo.Version);
            key.SetValue("Publisher", "AxoClient");
            key.SetValue("DisplayIcon", $"\"{exePath}\",0");
            key.SetValue("InstallLocation", _installLocation.Directory);
            key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("URLInfoAbout", AppInfo.Repository);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            if (File.Exists(exePath))
            {
                key.SetValue("EstimatedSize", (int)(new FileInfo(exePath).Length / 1024), RegistryValueKind.DWord);
            }
        }

        /// <summary>.lnk über das Windows-Skripting-Objekt (WScript.Shell), keine zusätzliche Bibliothek nötig.</summary>
        private void createShortcut(string path, string target)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
            {
                return;
            }

            object? shell = null;
            object? link = null;
            try
            {
                shell = Activator.CreateInstance(type);
                link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [path]);
                if (link == null)
                {
                    return;
                }

                var linkType = link.GetType();
                void Set(string name, object value) =>
                    linkType.InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, link, [value]);
                Set("TargetPath", target);
                Set("WorkingDirectory", Path.GetDirectoryName(target)!);
                Set("IconLocation", target + ",0");
                Set("Description", "AxoClient");
                linkType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
            }
            catch (Exception exception) when (exception is COMException or System.Reflection.TargetInvocationException or IOException or UnauthorizedAccessException)
            {
                _appLog.Warn($"Verknüpfung {path}", exception);
            }
            finally
            {
                if (link != null && Marshal.IsComObject(link))
                {
                    Marshal.FinalReleaseComObject(link);
                }

                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }

        /// <summary>
        /// Frühere Installationen über Velopack lagen in <c>%LocalAppData%\AxoClient</c> (nur Programmdateien, die Daten
        /// liegen in %AppData%). Verknüpfungen und der Apps-Eintrag wurden oben schon überschrieben.
        /// </summary>
        private void removeOldVelopackInstall()
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AxoClient");
            if (!Directory.Exists(directory))
            {
                return;
            }

            if (!File.Exists(Path.Combine(directory, "Update.exe")) && !Directory.Exists(Path.Combine(directory, "current"))
                                                               && !Directory.Exists(Path.Combine(directory, "packages")))
            {
                return;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _appLog.Warn("Alte Velopack-Installation entfernen", exception);
            }
        }

        private void tryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _appLog.Warn($"{path} löschen", exception);
            }
        }
    }
}
