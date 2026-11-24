using AxoClient.Accounts.Contract;
using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Settings.Contract;
using AxoClient.Social.Contract;
using AxoClient.Updates.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich App.</summary>
    public class AppHandler : BridgeHandler
    {
        private readonly IDevAuthProvider _devAuth;
        private readonly ISettingsService _settings;
        private readonly IAccountManager _accounts;
        private readonly INotificationManager _notifications;
        private readonly IUpdateService _launcherUpdates;
        private readonly IJobManager _jobs;
        private readonly IGameLauncher _games;
        private readonly IOnlineClient _online;
        private readonly IAxoModInstaller _axoMod;
        private readonly AppPaths _paths;
        private readonly IWorldManager _worlds;
        private readonly FileRefs _files;
        private readonly IAppInfo _appInfo;
        private readonly ISystemInfo _systemInfo;
        private readonly IAppearance _appearance;
        private readonly IImageFormats _imageFormats;
        private readonly IPathGuard _pathGuard;
        private readonly ILogRedactor _logRedactor;

        public AppHandler(
            IDevAuthProvider devAuth,
            ISettingsService settings,
            IAccountManager accounts,
            INotificationManager notifications,
            IUpdateService launcherUpdates,
            IJobManager jobs,
            IGameLauncher games,
            IOnlineClient online,
            IAxoModInstaller axoMod,
            AppPaths paths,
            IWorldManager worlds,
            FileRefs files,
            IAppInfo appInfo,
            ISystemInfo systemInfo,
            IAppearance appearance,
            IImageFormats imageFormats,
            IPathGuard pathGuard,
            ILogRedactor logRedactor,
            IAppLog appLog,
            ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _devAuth = devAuth;
            _settings = settings;
            _accounts = accounts;
            _notifications = notifications;
            _launcherUpdates = launcherUpdates;
            _jobs = jobs;
            _games = games;
            _online = online;
            _axoMod = axoMod;
            _paths = paths;
            _worlds = worlds;
            _files = files;
            _appInfo = appInfo;
            _systemInfo = systemInfo;
            _appearance = appearance;
            _imageFormats = imageFormats;
            _pathGuard = pathGuard;
            _logRedactor = logRedactor;
        }

        // Quellbild vor dem Zuschnitt; gespeichert wird später nur das kleine zugeschnittene Ergebnis
        private const long MaxPickedImageBytes = 25L * 1024 * 1024;

        public override void Register(MessageRouter r, MainWindow window)
        {
            r.On("app.init", () => new
            {
                version = _appInfo.Version,
                shortVersion = _appInfo.ShortVersion,
                settings = _settings.Current,
                state = _settings.State,
                accounts = _accounts.Views(),
                needsLogin = _accounts.Active == null || _accounts.ActiveNeedsLogin,
                setupDone = _settings.Current.SetupDone,
                devAuth = _devAuth.Enabled,
                curseForge = AppInfo.CurseForgeEnabled,
                unread = _notifications.UnreadCount,
                update = _launcherUpdates.Available,
                jobs = _jobs.Active,
                running = _games.Running,
                serviceAvailable = _online.Available,
                totalRamMb = _systemInfo.Memory().TotalMb,
                axoModVersions = _axoMod.SupportedMinecraftVersions,
                presets = _appearance.Presets
            });

            r.On("window.minimize", () =>
            {
                window.Minimize();
                return null;
            });
            r.On("window.toggleMaximize", () =>
            {
                window.ToggleMaximize();
                return null;
            });
            r.On("window.close", () =>
            {
                window.Close();
                return null;
            });
            r.On("window.drag", () =>
            {
                window.Drag();
                return null;
            });
            r.On<EdgeData>("window.resize", (d, _) =>
            {
                window.Resize(d.Edge);
                return Task.FromResult<object?>(null);
            });
            r.On("window.state", () => new { maximized = window.IsMaximized });

            r.On<UrlData>("shell.openUrl", (d, _) =>
            {
                OpenUrl(d.Url);
                return Task.FromResult<object?>(null);
            });

            r.On<FolderData>("shell.openFolder", async (d, ct) =>
            {
                var p = _paths;
                string path = d.Kind switch
                {
                    "instance" => p.GameDir(TextHelper.RequireId(d.Id)),
                    "instanceSub" when d.Sub is "mods" or "resourcepacks" or "shaderpacks" or "saves" or "screenshots" or "config" or "logs"
                        or "crash-reports" => Path.Combine(p.GameDir(TextHelper.RequireId(d.Id)), d.Sub),
                    "world" => _worlds.WorldDir(TextHelper.RequireId(d.Id), d.Sub ?? ""),
                    "worldDatapacks" => Directory.CreateDirectory(Path.Combine(_worlds.WorldDir(TextHelper.RequireId(d.Id), d.Sub ?? ""), "datapacks")).FullName,
                    "server" => p.ServerData(TextHelper.RequireId(d.Id)),
                    "serverSub" when d.Sub is "plugins" or "mods" or "logs" or "world" => Path.Combine(p.ServerData(TextHelper.RequireId(d.Id)), d.Sub),
                    "servers" => p.Servers,
                    "logs" => p.Logs,
                    "data" => p.Root,
                    _ => throw new AxoException("errors.invalidInput")
                };
                Directory.CreateDirectory(path);
                OpenInExplorer(path);
                await Task.CompletedTask;
                return null;
            });

            r.On("jobs.list", () => _jobs.Active);
            r.On<JobData>("jobs.cancel", (d, _) => Task.FromResult<object?>(_jobs.Cancel(d.Id)));

            r.On<OpenFileData>("dialog.openFile", async (d, _) =>
            {
                (string, string[])[] filters = d.Purpose switch
                {
                    "import" => [("Instanzen (.mrpack, .zip)", ["*.mrpack", "*.zip"])],
                    "mod" => [("Mods (.jar)", ["*.jar"])],
                    "pack" => [("Ressourcepacks und Shader (.zip)", ["*.zip"])],
                    "world" => [("Welten (.zip)", ["*.zip"])],
                    "image" => [("Bilder (.png, .jpg)", ["*.png", "*.jpg", "*.jpeg"])],
                    "skin" or "cape" => [("PNG-Bild", ["*.png"])],
                    "emote" => [("Animationen (.json)", ["*.json"])],
                    "java" => [("Java (javaw.exe)", ["javaw.exe", "java.exe"])],
                    _ => throw new AxoException("errors.invalidInput")
                };
                var files = await window.OpenFile(d.Purpose switch
                {
                    "import" => "Instanz importieren",
                    "java" => "Java auswählen",
                    _ => "Datei auswählen"
                }, filters, d.Multi);
                return files.Where(File.Exists).Select(f => new { id = _files.Add(f), name = Path.GetFileName(f), size = new FileInfo(f).Length })
                    .ToList();
            });

            // Bild zum Zuschneiden: nativ gewählt, damit die Auswahl zum Launcher-Fenster gehört und nicht dahinter aufgeht
            r.On("dialog.openImage", async ct =>
            {
                var files = await window.OpenFile("Bild auswählen", [("Bilder (.png, .jpg)", ["*.png", "*.jpg", "*.jpeg"])], false);
                var path = files.FirstOrDefault(File.Exists);
                if (path == null)
                {
                    return null;
                }

                if (new FileInfo(path).Length > MaxPickedImageBytes)
                {
                    throw new AxoException("errors.fileTooLarge");
                }

                var bytes = await File.ReadAllBytesAsync(path, ct);
                if (!_imageFormats.IsPngOrJpeg(bytes))
                {
                    throw new AxoException("errors.invalidImage");
                }

                return new { name = Path.GetFileName(path), base64 = Convert.ToBase64String(bytes) };
            });

            r.On<SaveFileData>("dialog.saveFile", async (d, _) =>
            {
                (string, string[])[] filters = d.Purpose switch
                {
                    "mrpack" => [("Modrinth-Modpack (.mrpack)", ["*.mrpack"])],
                    "backup" => [("Backup (.zip)", ["*.zip"])],
                    "log" => [("Textdatei (.txt)", ["*.txt"])],
                    _ => throw new AxoException("errors.invalidInput")
                };
                var name = TextHelper.Clean(d.Name, 80);
                var path = await window.SaveFile("Speichern unter", _pathGuard.IsSafeFileName(name) ? name : "AxoClient", filters);
                return path == null ? null : new { id = _files.Add(path), name = Path.GetFileName(path) };
            });

            r.On<UploadData>("files.upload", async (d, ct) =>
            {
                var id = await _files.Upload(d.Name, d.Base64, ct);
                return new { id, name = _files.NameOf(id) };
            });

            // Texte zum Kopieren immer geschwärzt (Tokens, Spielername, UUID, IP)
            r.On<TextData>("text.redact", (d, _) => Task.FromResult<object?>(_logRedactor.RedactForSharing(d.Text)));

            // Zwischenablage, falls der Browser sie im eigenen Schema nicht erlaubt (WinForms braucht einen STA-Thread)
            r.On<TextData>("clipboard.write", (d, _) =>
            {
                var text = d.Text.Length > 2_000_000 ? d.Text[..2_000_000] : d.Text;
                var tcs = new TaskCompletionSource<object?>();
                var thread = new Thread(() =>
                {
                    try
                    {
                        System.Windows.Forms.Clipboard.SetText(text);
                        tcs.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                return tcs.Task;
            });
        }
    }
}
