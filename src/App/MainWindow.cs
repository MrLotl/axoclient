using System.Text.Json;
using AxoClient.App.Bridge;
using AxoClient.App.Overlay;
using AxoClient.Common;
using AxoClient.DataModel.Settings;
using AxoClient.DataModel.Social;
using AxoClient.Integrations.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Servers.Contract;
using AxoClient.Settings.Contract;
using AxoClient.Social.Contract;
using Photino.NET;

namespace AxoClient.App
{
    public sealed class MainWindow
    {
        private readonly AppHost _host;
        private readonly SingleInstance _single;
        private readonly IAppLog _appLog;
        private readonly IJsonHelper _jsonHelper;
        private readonly ISettingsService _settings;
        private readonly IGameLauncher _games;
        private readonly ILocalServerManager _localServers;
        private readonly UiServer _server;
        private readonly WindowChrome _windowChrome;
        private readonly PhotinoWindow _window;
        private readonly MessageRouter _router;
        private readonly EventForwarder _forwarder;
        private readonly Queue<string> _pending = new();
        private bool _pageReady;
        private bool _hiddenForGame;

        public MainWindow(AppHost host, SingleInstance single, IAppLog appLog, IJsonHelper jsonHelper, IExceptionHelper exceptionHelper,
            AppPaths paths, IEventHub events, ISettingsService settings, IGameLauncher games, INotificationManager notifications,
            IDiscordIntegration discord, ILocalServerManager localServers, UiServer server, WindowChrome windowChrome, GameWindow gameWindow,
            ToastTexts toastTexts, PrivateOverlay privateOverlay, IEnumerable<BridgeHandler> handlers)
        {
            _host = host;
            _single = single;
            _appLog = appLog;
            _jsonHelper = jsonHelper;
            _settings = settings;
            _games = games;
            _localServers = localServers;
            _server = server;
            _windowChrome = windowChrome;
            var (x, y, width, height) = windowChrome.StartBounds(1280, 800);
            _window = new PhotinoWindow()
                .SetTitle("AxoClient")
                .SetChromeless(true)
                .SetResizable(true)
                .SetUseOsDefaultSize(false)
                .SetUseOsDefaultLocation(false)
                .SetSize(width, height)
                .SetLocation(new System.Drawing.Point(x, y))
                .SetMinSize(1060, 680)
                .SetContextMenuEnabled(false)
                .SetDevToolsEnabled(Environment.GetEnvironmentVariable("AXOCLIENT_DEVTOOLS") == "1")
                .SetJavascriptClipboardAccessEnabled(true)
                .SetGrantBrowserPermissions(false)
                .SetMediaStreamEnabled(false)
                .SetFileSystemAccessEnabled(false)
                .SetNotificationsEnabled(false)
                .SetTemporaryFilesPath(Path.Combine(paths.Cache, "webview"))
                .RegisterWebMessageReceivedHandler((_, message) => onWebMessage(message))
                .RegisterWindowCreatedHandler((_, _) => windowChrome.KeepTaskbarVisible(_window!.WindowHandle))
                .RegisterMaximizedHandler((_, _) => _router!.Emit("window.state", new { maximized = true }))
                .RegisterRestoredHandler((_, _) => _router!.Emit("window.state", new { maximized = false }))
                .RegisterWindowClosingHandler((_, _) => onClosing());

            if (windowIcon(paths.Cache) is { } icon)
            {
                _window.SetIconFile(icon);
            }

            // Privater Modus der Mod: eigenes Fenster über dem Spiel, das Windows aus Aufnahmen heraushält
            if (privateOverlay.Supported)
            {
                privateOverlay.Start();
            }

            _router = new MessageRouter(send, jsonHelper, appLog, exceptionHelper);
            _forwarder = new EventForwarder(events, _router);
            foreach (var handler in handlers)
            {
                handler.Register(_router, this);
            }

            single.Activated += _ => Invoke(BringToFront);
            // Das Axo-Menü im Spiel holt den Launcher nach vorn (z. B. "Fehlende Mods installieren")
            events.Published += (type, _) =>
            {
                if (type == "app.focus")
                {
                    Invoke(BringToFront);
                }
            };
            games.GameStarted += game =>
            {
                if (settings.Current.Game.WindowMode == WindowMode.Maximized)
                {
                    gameWindow.MaximizeWhenShown(game.Pid);
                }
                Invoke(onGameStarted);
            };
            games.GameExited += _ => Invoke(afterGameExit);
            notifications.IsInBackground = () => windowChrome.IsHiddenOrMinimized(_window.WindowHandle);
            notifications.IsPlaying = () => games.AnyRunning;
            notifications.ShowToast = item => Invoke(() => _window.SendNotification("AxoClient", toastTexts.Text(item)));
            discord.JoinRequested += (address, _) => _router.Emit("discord.join", new { address });
        }

        public IntPtr Handle => _window.WindowHandle;

        public void Run()
        {
            _ = _host.Start().ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    _appLog.Error("Dienste konnten nicht starten", t.Exception);
                }

                _router.Emit("app.ready", null);
            }, TaskScheduler.Default);
            _server.Start();
            _window.Load(_server.BaseUri);
            _window.WaitForClose();
            _forwarder.Dispose();
            _server.Dispose();
        }

        private void send(string message)
        {
            lock (_pending)
            {
                if (!_pageReady)
                {
                    if (_pending.Count < 1000)
                    {
                        _pending.Enqueue(message);
                    }
                    return;
                }
            }
            post(message);
        }

        private void onWebMessage(string message)
        {
            string[] queued = [];
            lock (_pending)
            {
                if (!_pageReady)
                {
                    _pageReady = true;
                    queued = _pending.ToArray();
                    _pending.Clear();
                }
            }
            foreach (var m in queued)
            {
                post(m);
            }
            _router.Handle(message);
        }

        private void post(string message)
        {
            try
            {
                _window.SendWebMessage(message);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or ApplicationException)
            {
                // Fenster wird gerade geschlossen
            }
        }

        public void Invoke(Action action)
        {
            try
            {
                _window.Invoke(action);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or ApplicationException)
            {
            }
        }

        public void Minimize() => Invoke(() => _window.SetMinimized(true));

        public void ToggleMaximize() => Invoke(() => _window.SetMaximized(!_window.Maximized));

        public void Close() => Invoke(_window.Close);

        public void Drag() => Invoke(() => _windowChrome.Drag(_window.WindowHandle));

        public void Resize(string edge) => Invoke(() => _windowChrome.Resize(_window.WindowHandle, edge));

        public void BringToFront()
        {
            _hiddenForGame = false;
            _windowChrome.BringToFront(_window.WindowHandle);
        }

        public bool IsMaximized => _window.Maximized;

        public Task<string[]> OpenFile(string title, (string Name, string[] Extensions)[] filters, bool multi)
        {
            var tcs = new TaskCompletionSource<string[]>();
            Invoke(() =>
            {
                try
                {
                    tcs.TrySetResult(_window.ShowOpenFile(title, null, multi, filters) ?? []);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        public Task<string?> OpenFolder(string title)
        {
            var tcs = new TaskCompletionSource<string?>();
            Invoke(() =>
            {
                try
                {
                    tcs.TrySetResult(_window.ShowOpenFolder(title, null, false)?.FirstOrDefault());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        public Task<string?> SaveFile(string title, string suggestedName, (string Name, string[] Extensions)[] filters)
        {
            var tcs = new TaskCompletionSource<string?>();
            var owner = _window.WindowHandle;
            var thread = new Thread(() =>
            {
                try
                {
                    using var dialog = new System.Windows.Forms.SaveFileDialog
                    {
                        Title = title,
                        FileName = suggestedName,
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        Filter = string.Join("|", filters.Select(f => $"{f.Name}|{string.Join(";", f.Extensions)}")),
                        AddExtension = true,
                        OverwritePrompt = true
                    };
                    tcs.TrySetResult(dialog.ShowDialog(new Win32Owner(owner)) == System.Windows.Forms.DialogResult.OK ? dialog.FileName : null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return tcs.Task;
        }

        private void onGameStarted()
        {
            switch (_settings.Current.AfterLaunch)
            {
                case AfterLaunch.Minimize:
                    _window.SetMinimized(true);
                    break;
                case AfterLaunch.Hide:
                    _hiddenForGame = true;
                    _windowChrome.Hide(_window.WindowHandle);
                    break;
                case AfterLaunch.Close:
                    if (!_localServers.AnyRunning)
                    {
                        _hiddenForGame = true;
                        _windowChrome.Hide(_window.WindowHandle);
                    }
                    break;
            }
        }

        private void afterGameExit()
        {
            if (_games.AnyRunning)
            {
                return;
            }

            if (_settings.Current.AfterLaunch == AfterLaunch.Close && _hiddenForGame)
            {
                _window.Close();
                return;
            }
            if (_hiddenForGame || _settings.Current.AfterLaunch == AfterLaunch.Minimize)
            {
                BringToFront();
            }
        }

        private bool onClosing()
        {
            _router.Emit("app.closing", null);
            return false;
        }

        public string Serialize(object? value) => JsonSerializer.Serialize(value, _jsonHelper.Compact);

        /// <summary>Photino will eine Datei: das eingebettete Symbol einmal in den Cache schreiben.</summary>
        private string? windowIcon(string cacheDir)
        {
            try
            {
                using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("axoclient.ico");
                if (stream == null)
                {
                    return null;
                }

                var path = Path.Combine(cacheDir, "axoclient.ico");
                if (!File.Exists(path) || new FileInfo(path).Length != stream.Length)
                {
                    Directory.CreateDirectory(cacheDir);
                    using var file = File.Create(path);
                    stream.CopyTo(file);
                }
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _appLog.Warn("Fenstersymbol", ex);
                return null;
            }
        }
    }

    /// <summary>Kurze deutsche Texte für Windows-Benachrichtigungen.</summary>
    public class ToastTexts
    {
        public string Text(NotificationItem item)
        {
            string P(string key) => item.Text.Params?.GetValueOrDefault(key)?.ToString() ?? "";
            return item.Type switch
            {
                "friendRequest" => $"{P("name")} möchte mit dir befreundet sein.",
                "friendAccepted" => $"{P("name")} hat deine Anfrage angenommen.",
                "friendOnline" => $"{P("name")} ist online.",
                "friendOnServer" => $"{P("name")} spielt auf {P("server")}.",
                "friendOpenWorld" => $"{P("name")} hat eine Welt zum Nachjoinen geöffnet.",
                "invite" => $"{P("name")} lädt dich ein.",
                "inbox" => $"{P("name")} hat dir etwas geschickt.",
                "modUpdates" => $"Updates für {P("instance")} verfügbar.",
                "modpackUpdates" => $"Modpack-Update für {P("instance")} verfügbar.",
                "launcherUpdate" => $"AxoClient {P("version")} ist verfügbar.",
                "jobFinished" => "Download abgeschlossen.",
                "authExpired" => "Bitte melde dich erneut an.",
                "lowDiskSpace" => "Der Speicherplatz wird knapp.",
                "serverCrashed" => $"Der Server {P("server")} wurde unerwartet beendet.",
                "serverPlayerJoined" => $"{P("player")} ist auf {P("server")}.",
                "capeReview" => "Dein Cape wurde geprüft.",
                _ => "Neue Benachrichtigung"
            };
        }
    }

    internal sealed class Win32Owner : System.Windows.Forms.IWin32Window
    {
        public Win32Owner(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
