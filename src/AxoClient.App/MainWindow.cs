using System.Text.Json;
using AxoClient.App.Bridge;
using AxoClient.Core.Common;
using Photino.NET;

namespace AxoClient.App;

public sealed class MainWindow
{
    private readonly AppHost _host;
    private readonly SingleInstance _single;
    private readonly PhotinoWindow _window;
    private readonly MessageRouter _router;
    private readonly EventForwarder _forwarder;
    private readonly UiServer _server;
    private readonly Queue<string> _pending = new();
    private bool _pageReady;
    private bool _hiddenForGame;
    /// <summary>Fenster für die privaten Widgets der Mod (nur Windows 10 2004 und neuer).</summary>
    internal Overlay.PrivateOverlay? PrivateOverlay { get; }

    public MainWindow(AppHost host, SingleInstance single)
    {
        _host = host;
        _single = single;
        _server = new UiServer(new SchemeHandler(host));
        var (x, y, width, height) = WindowChrome.StartBounds(1280, 800);
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
            .SetTemporaryFilesPath(Path.Combine(host.Paths.Cache, "webview"))
            .RegisterWebMessageReceivedHandler((_, message) => OnWebMessage(message))
            .RegisterWindowCreatedHandler((_, _) => WindowChrome.KeepTaskbarVisible(_window!.WindowHandle))
            .RegisterMaximizedHandler((_, _) => _router!.Emit("window.state", new { maximized = true }))
            .RegisterRestoredHandler((_, _) => _router!.Emit("window.state", new { maximized = false }))
            .RegisterWindowClosingHandler((_, _) => OnClosing());

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "axoclient.ico");
        if (File.Exists(icon))
        {
            _window.SetIconFile(icon);
        }

        // Privater Modus der Mod: eigenes Fenster über dem Spiel, das Windows aus Aufnahmen heraushält
        if (Overlay.PrivateOverlay.Supported)
        {
            AxoClient.Core.Integrations.AxoModBridge.PrivateOverlay = true;
            PrivateOverlay = new Overlay.PrivateOverlay(host);
        }

        _router = new MessageRouter(Send);
        _forwarder = new EventForwarder(host.Events, _router);
        Handlers.Register(_router, new HandlerContext(host, this, new FileRefs(host.Paths)));

        single.Activated += _ => Invoke(BringToFront);
        // Das Axo-Menü im Spiel holt den Launcher nach vorn (z. B. "Fehlende Mods installieren")
        host.Events.Published += (type, _) =>
        {
            if (type == "app.focus")
                Invoke(BringToFront);
        };
        host.Games.GameStarted += _ => Invoke(OnGameStarted);
        host.Games.GameExited += _ => Invoke(AfterGameExit);
        host.Notifications.IsInBackground = () => WindowChrome.IsHiddenOrMinimized(_window.WindowHandle);
        host.Notifications.IsPlaying = () => host.Games.AnyRunning;
        host.Notifications.ShowToast = item => Invoke(() => _window.SendNotification("AxoClient", Toasts.Text(item)));
        host.Discord.JoinRequested += (address, _) => _router.Emit("discord.join", new { address });
    }

    public IntPtr Handle => _window.WindowHandle;

    public void Run()
    {
        _ = _host.StartAsync().ContinueWith(t =>
        {
            if (t.Exception != null)
                AppLog.Error("Dienste konnten nicht starten", t.Exception);
            _router.Emit("app.ready", null);
        }, TaskScheduler.Default);
        _server.Start();
        _window.Load(_server.BaseUri);
        _window.WaitForClose();
        _forwarder.Dispose();
        _server.Dispose();
    }

    private void Send(string message)
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
        Post(message);
    }

    private void OnWebMessage(string message)
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
            Post(m);
        }
        _router.Handle(message);
    }

    private void Post(string message)
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

    public void Drag() => Invoke(() => WindowChrome.Drag(_window.WindowHandle));

    public void Resize(string edge) => Invoke(() => WindowChrome.Resize(_window.WindowHandle, edge));

    public void BringToFront()
    {
        _hiddenForGame = false;
        WindowChrome.BringToFront(_window.WindowHandle);
    }

    public bool IsMaximized => _window.Maximized;

    public Task<string[]> OpenFileAsync(string title, (string Name, string[] Extensions)[] filters, bool multi)
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

    public Task<string?> OpenFolderAsync(string title)
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

    public Task<string?> SaveFileAsync(string title, string suggestedName, (string Name, string[] Extensions)[] filters)
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

    private void OnGameStarted()
    {
        switch (_host.Settings.Current.AfterLaunch)
        {
            case AfterLaunch.Minimize:
                _window.SetMinimized(true);
                break;
            case AfterLaunch.Hide:
                _hiddenForGame = true;
                WindowChrome.Hide(_window.WindowHandle);
                break;
            case AfterLaunch.Close:
                if (!_host.LocalServers.AnyRunning)
                {
                    _hiddenForGame = true;
                    WindowChrome.Hide(_window.WindowHandle);
                }
                break;
        }
    }

    private void AfterGameExit()
    {
        if (_host.Games.AnyRunning)
            return;
        if (_host.Settings.Current.AfterLaunch == AfterLaunch.Close && _hiddenForGame)
        {
            _window.Close();
            return;
        }
        if (_hiddenForGame || _host.Settings.Current.AfterLaunch == AfterLaunch.Minimize)
            BringToFront();
    }

    private bool OnClosing()
    {
        _router.Emit("app.closing", null);
        return false;
    }

    public static string Serialize(object? value) => JsonSerializer.Serialize(value, Json.Compact);
}

internal static class Toasts
{
    public static string Text(Core.Social.NotificationItem item)
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

internal sealed class Win32Owner(IntPtr handle) : System.Windows.Forms.IWin32Window
{
    public IntPtr Handle { get; } = handle;
}
