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

        public override void Register(MessageRouter router, MainWindow window)
        {
            router.On("app.init", () => new
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

            router.On("window.minimize", () =>
            {
                window.Minimize();
                return null;
            });
            router.On("window.toggleMaximize", () =>
            {
                window.ToggleMaximize();
                return null;
            });
            router.On("window.close", () =>
            {
                window.Close();
                return null;
            });
            router.On("window.drag", () =>
            {
                window.Drag();
                return null;
            });
            router.On<EdgeData>("window.resize", (request, _) =>
            {
                window.Resize(request.Edge);
                return Task.FromResult<object?>(null);
            });
            router.On("window.state", () => new { maximized = window.IsMaximized });

            router.On<UrlData>("shell.openUrl", (request, _) =>
            {
                OpenUrl(request.Url);
                return Task.FromResult<object?>(null);
            });

            router.On<FolderData>("shell.openFolder", async (request, cancellationToken) =>
            {
                var paths = _paths;
                string path = request.Kind switch
                {
                    "instance" => paths.GameDirectory(TextHelper.RequireId(request.Id)),
                    "instanceSub" when request.Sub is "mods" or "resourcepacks" or "shaderpacks" or "saves" or "screenshots" or "config" or "logs"
                        or "crash-reports" => Path.Combine(paths.GameDirectory(TextHelper.RequireId(request.Id)), request.Sub),
                    "world" => _worlds.WorldDirectory(TextHelper.RequireId(request.Id), request.Sub ?? ""),
                    "worldDatapacks" => Directory.CreateDirectory(Path.Combine(_worlds.WorldDirectory(TextHelper.RequireId(request.Id), request.Sub ?? ""), "datapacks")).FullName,
                    "server" => paths.ServerData(TextHelper.RequireId(request.Id)),
                    "serverSub" when request.Sub is "plugins" or "mods" or "logs" or "world" => Path.Combine(paths.ServerData(TextHelper.RequireId(request.Id)), request.Sub),
                    "servers" => paths.Servers,
                    "logs" => paths.Logs,
                    "data" => paths.Root,
                    _ => throw new AxoException("errors.invalidInput")
                };
                Directory.CreateDirectory(path);
                OpenInExplorer(path);
                await Task.CompletedTask;
                return null;
            });

            router.On("jobs.list", () => _jobs.Active);
            router.On<JobData>("jobs.cancel", (request, _) => Task.FromResult<object?>(_jobs.Cancel(request.Id)));

            router.On<OpenFileData>("dialog.openFile", async (request, _) =>
            {
                (string, string[])[] filters = request.Purpose switch
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
                var files = await window.OpenFile(request.Purpose switch
                {
                    "import" => "Instanz importieren",
                    "java" => "Java auswählen",
                    _ => "Datei auswählen"
                }, filters, request.Multi);
                return files.Where(File.Exists).Select(file => new { id = _files.Add(file), name = Path.GetFileName(file), size = new FileInfo(file).Length })
                    .ToList();
            });

            // Bild zum Zuschneiden: nativ gewählt, damit die Auswahl zum Launcher-Fenster gehört und nicht dahinter aufgeht
            router.On("dialog.openImage", async cancellationToken =>
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

                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                if (!_imageFormats.IsPngOrJpeg(bytes))
                {
                    throw new AxoException("errors.invalidImage");
                }

                return new { name = Path.GetFileName(path), base64 = Convert.ToBase64String(bytes) };
            });

            router.On<SaveFileData>("dialog.saveFile", async (request, _) =>
            {
                (string, string[])[] filters = request.Purpose switch
                {
                    "mrpack" => [("Modrinth-Modpack (.mrpack)", ["*.mrpack"])],
                    "backup" => [("Backup (.zip)", ["*.zip"])],
                    "log" => [("Textdatei (.txt)", ["*.txt"])],
                    _ => throw new AxoException("errors.invalidInput")
                };
                var name = TextHelper.Clean(request.Name, 80);
                var path = await window.SaveFile("Speichern unter", _pathGuard.IsSafeFileName(name) ? name : "AxoClient", filters);
                return path == null ? null : new { id = _files.Add(path), name = Path.GetFileName(path) };
            });

            router.On<UploadData>("files.upload", async (request, cancellationToken) =>
            {
                var id = await _files.Upload(request.Name, request.Base64, cancellationToken);
                return new { id, name = _files.NameOf(id) };
            });

            // Texte zum Kopieren immer geschwärzt (Tokens, Spielername, UUID, IP)
            router.On<TextData>("text.redact", (request, _) => Task.FromResult<object?>(_logRedactor.RedactForSharing(request.Text)));

            // Zwischenablage, falls der Browser sie im eigenen Schema nicht erlaubt (WinForms braucht einen STA-Thread)
            router.On<TextData>("clipboard.write", (request, _) =>
            {
                var text = request.Text.Length > 2_000_000 ? request.Text[..2_000_000] : request.Text;
                var completionSource = new TaskCompletionSource<object?>();
                var thread = new Thread(() =>
                {
                    try
                    {
                        System.Windows.Forms.Clipboard.SetText(text);
                        completionSource.TrySetResult(null);
                    }
                    catch (Exception exception)
                    {
                        completionSource.TrySetException(exception);
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                return completionSource.Task;
            });
        }
    }
}
