using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace AxoClient.UI;

public partial class MainWindow : Window
{
    private readonly AppServices _app;
    private readonly TaskCompletionSource _sessionRestored = new();
    private readonly Dictionary<RadioButton, UserControl> _pages;
    private TrayIcon? _tray;
    private bool _hiddenForGame;
    private int _dragDepth;
    private bool _restoring = true;

    public MainWindow()
    {
        InitializeComponent();
        _app = new AppServices(DialogHost);
        _pages = new Dictionary<RadioButton, UserControl>
        {
            [NavHome] = HomePage,
            [NavInstances] = InstancesPage,
            [NavServers] = LocalServersPage,
            [NavSkins] = SkinsPage,
            [NavConsole] = GameLogPage,
            [NavSettings] = SettingsPage
        };

        AccountMenu.Initialize(_app);
        AccountMenu.CloseRequested += () => AccountPopup.IsOpen = false;
        HomePage.Initialize(_app);
        InstancesPage.Initialize(_app);
        LocalServersPage.Initialize(_app);
        SkinsPage.Initialize(_app);
        GameLogPage.Initialize(_app);
        SettingsPage.Initialize(_app);

        LocalServersPage.JoinRequested += (address, version) =>
        {
            NavHome.IsChecked = true;
            _ = HomePage.JoinServerAsync(address, version, null, ask: false);
        };
        LocalServersPage.ConsoleRequested += server =>
        {
            NavConsole.IsChecked = true;
            GameLogPage.ShowServer(server);
        };

        _app.Accounts.Changed += UpdateAccount;
        HomePage.OpenInstanceRequested += (inst, section) =>
        {
            NavInstances.IsChecked = true;
            InstancesPage.Open(inst, section);
        };
        HomePage.Notifications.Changed += () => SetUnreadCount(HomePage.Notifications.UnreadCount);
        HomePage.Notifications.Arrived += OnNoticeArrived;
        DialogHost.OpenChanged += open =>
            ShellContent.Effect = open ? new BlurEffect { Radius = 3, KernelType = KernelType.Gaussian } : null;

        InstancesPage.PlayRequested += (inst, quickPlay) =>
        {
            _app.Instances.Select(inst);
            NavHome.IsChecked = true;
            _ = HomePage.PlayAsync(quickPlay);
        };
        _app.Discord.JoinRequested += (server, version) => Dispatcher.InvokeAsync(() =>
        {
            NavHome.IsChecked = true;
            BringToFront();
            _ = JoinFromDiscordAsync(server, version);
        });
        _app.Discord.Initialize(_app.Settings);
        _app.Games.Started += OnGameStarted;
        _app.Games.Crashed += inst => _ = OnGameCrashedAsync(inst);
        _app.Games.Changed += () => Dispatcher.InvokeAsync(() =>
        {
            if (_hiddenForGame && !_app.Games.AnyRunning)
                BringToFront();
        });

        Autostart.Refresh();
        StartMenu.EnsureShortcut();
        if (Autostart.StartedByWindows && _app.Settings.AutostartMinimized)
            WindowState = WindowState.Minimized;
        UpdateAccount();
    }

    public IDialogService Dialogs => DialogHost;

    public AppServices App => _app;

    public void ShowToast(Toast toast) => Toasts.Show(toast);

    public void Navigate(string page)
    {
        var target = page switch
        {
            "instances" => NavInstances,
            "servers" => NavServers,
            "skins" => NavSkins,
            "console" => NavConsole,
            "settings" => NavSettings,
            _ => NavHome
        };
        target.IsChecked = true;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = "v" + AppInfo.ShortVersion;
        Updater.CleanUp();

        if (LauncherSettings.LoadProblem is { } problem)
            await Dialogs.ShowErrorAsync("Einstellungen konnten nicht gelesen werden", problem.Error, problem.Text);

        var firstStart = !_app.Settings.SetupDone;
        if (firstStart)
            Welcome.ShowFirstStart(_app);
        var updateCheck = CheckForUpdateAsync();
        await _app.Accounts.TryRestoreAsync();
        _restoring = false;
        UpdateAccount();
        if (!firstStart && _app.Accounts.Session == null)
            Welcome.ShowWelcomeBack(_app);
        _sessionRestored.TrySetResult();
        _ = HomePage.Notifications.RefreshAsync();
        _ = ScanModUpdatesAsync();
        await updateCheck;
    }

    private async Task CheckForUpdateAsync()
    {
        var update = await UpdateDialog.CheckQuietlyAsync(_app);
        if (update == null)
            return;
        UpdatePill.Tag = update;
        UpdatePill.Visibility = Visibility.Visible;
        HomePage.Notifications.AddLocal(new Notice
        {
            Key = "update:" + update.Version,
            Kind = NoticeKind.AppUpdate,
            Title = $"AxoClient {update.Version} ist da",
            Text = "Klicke auf „Update verfügbar“ oben, um es zu installieren.",
            Update = update
        });
        if (_app.Settings.AutoUpdate)
            await UpdateDialog.InstallAsync(_app, update, Close);
    }

    private async void UpdatePill_Click(object sender, RoutedEventArgs e)
    {
        if (UpdatePill.Tag is UpdateInfo update)
            await UpdateDialog.ShowAsync(_app, update, Close);
    }

    private void UpdateAccount()
    {
        var session = _app.Accounts.Session;
        AccountName.Text = session?.Username ?? "";
        AccountHead.Source = _app.Accounts.Profile?.Head
                             ?? _app.Accounts.Saved.FirstOrDefault(a => a.Active)?.Head;
        Ui.Show(AccountButton, session != null);
        Ui.Show(LoginButton, session == null);
        LoginButton.IsEnabled = !_restoring;
        LoginText.Text = _restoring ? "Prüfe Anmeldung …" : "Anmelden";
    }

    private void OnNoticeArrived(Notice notice)
    {
        if (DialogHost.IsOpen || notice.Kind is NoticeKind.AppUpdate)
            return;
        var image = notice.HeadUrl != null ? new System.Windows.Media.Imaging.BitmapImage(new Uri(notice.HeadUrl)) : null;
        var (action, badge) = notice.Kind switch
        {
            NoticeKind.Invite => ("Beitreten", "Join"),
            NoticeKind.Share => ("Ansehen", "Send"),
            NoticeKind.ModUpdates => ("Ansehen", "Refresh"),
            NoticeKind.FriendRequest => ("Ansehen", "UserAdd"),
            _ => ((string?)null, "Bell")
        };
        Toasts.Show(new Toast(notice.Title, notice.Share?.Title ?? notice.Text, image, badge, action, () =>
        {
            if (notice.Kind == NoticeKind.Invite && notice.Remote?.Server is { } server)
            {
                NavHome.IsChecked = true;
                _ = HomePage.JoinServerAsync(server, notice.Remote.Version, notice.Remote.FromName, ask: true);
            }
            else
            {
                _ = HomePage.ShowNotificationsAsync();
            }
        }, notice.Kind == NoticeKind.Share ? 12 : 7));
    }

    private async Task ScanModUpdatesAsync()
    {
        if (_app.Instances.Selected is not { CanUseMods: true } inst)
            return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var result = await UpdateScanner.ScanAsync(_app, [inst],
                new WorkProgress(new Progress<string>(), new Progress<double>(), cts.Token));
            var updates = result.Updates;
            if (updates.Count == 0)
                return;
            HomePage.Notifications.AddLocal(new Notice
            {
                Key = $"mods:{inst.Id}:{string.Join(",", updates.Select(u => u.Title + " " + u.Change))}",
                Kind = NoticeKind.ModUpdates,
                Title = Formats.Count(updates.Count, "Mod-Update verfügbar", "Mod-Updates verfügbar"),
                Text = $"{Formats.Some(updates.Select(u => u.Title).ToList(), 2, " und ")} in „{inst.Name}“.",
                Image = InstanceIcons.Load(inst),
                Instance = inst,
                Count = updates.Count
            });
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Mod-Updates suchen", ex);
        }
    }

    public void SetUnreadCount(int count)
    {
        BellCount.Text = count > 9 ? "9+" : count.ToString();
        Ui.Show(BellBadge, count > 0);
        BellButton.ToolTip = count > 0 ? $"Benachrichtigungen ({count} neu)" : "Benachrichtigungen";
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        LoginText.Text = "Anmeldung läuft …";
        await UiRun.GuardAsync(_app, "Anmeldung fehlgeschlagen", _app.Accounts.LoginAsync);
        UpdateAccount();
    }

    private void AccountButton_Checked(object sender, RoutedEventArgs e)
    {
        AccountMenu.Refresh();
        AccountPopup.IsOpen = true;
        ((RotateTransform)AccountChevron.RenderTransform).Angle = 180;
    }

    private void AccountPopup_Closed(object? sender, EventArgs e)
    {
        ((RotateTransform)AccountChevron.RenderTransform).Angle = 0;
        Dispatcher.BeginInvoke(() => AccountButton.IsChecked = false, DispatcherPriority.Input);
    }

    private async void Bell_Click(object sender, RoutedEventArgs e) => await HomePage.ShowNotificationsAsync();

    private void OnGameStarted(Installation inst, Process process)
    {
        PrivacyOverlayHost.Attach(_app, process, inst.MinecraftVersion);
        switch (_app.Settings.AfterLaunch)
        {
            case AfterLaunchAction.Minimize:
                WindowState = WindowState.Minimized;
                break;
            case AfterLaunchAction.Hide:
                HideToTray();
                break;
            case AfterLaunchAction.Close:
                Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Background);
                break;
        }
    }

    private async Task OnGameCrashedAsync(Installation inst)
    {
        await Dispatcher.InvokeAsync(BringToFront);
        await CrashDialog.ShowAsync(_app, inst, justCrashed: true, () =>
        {
            _app.Instances.Select(inst);
            NavHome.IsChecked = true;
            _ = HomePage.PlayAsync();
        });
    }

    private async Task JoinFromDiscordAsync(string server, string? version)
    {
        await _sessionRestored.Task;
        await HomePage.JoinServerAsync(server, version, null, ask: true);
    }

    private void HideToTray()
    {
        if (_tray == null)
        {
            _tray = new TrayIcon();
            _tray.ShowRequested += () => Dispatcher.InvokeAsync(BringToFront);
            _tray.ExitRequested += () => Dispatcher.InvokeAsync(Close);
        }
        var firstTime = !_tray.Visible;
        _tray.Visible = true;
        _hiddenForGame = true;
        Hide();
        if (firstTime)
            _tray.ShowHint("AxoClient läuft im Hintergrund weiter und kommt zurück, sobald du das Spiel beendest.");
    }

    public void BringToFront()
    {
        _hiddenForGame = false;
        if (_tray != null)
            _tray.Visible = false;
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var files = e.Data.GetDataPresent(DataFormats.FileDrop) && !DropHandler.Busy;
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (!files)
            return;
        if (e.RoutedEvent == DragEnterEvent)
            _dragDepth++;
        DropOverlay.Visibility = Visibility.Visible;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        if (--_dragDepth <= 0)
        {
            _dragDepth = 0;
            DropOverlay.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        _dragDepth = 0;
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            await DropHandler.HandleAsync(_app, paths);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) =>
        NativeWindows.UseDarkRoundedFrame(new WindowInteropHelper(this).Handle);

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        var frame = SystemParameters.WindowResizeBorderThickness;
        RootGrid.Margin = maximized
            ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4)
            : new Thickness(0);
        MaximizeIcon.Kind = maximized ? "WinRestore" : "WinMax";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private bool _closeConfirmed;

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel)
            return;
        var running = _app.LocalServers.Servers.Where(_app.LocalServers.IsRunning).ToList();
        if (running.Count == 0)
            return;
        e.Cancel = true;
        var names = string.Join(", ", running.Select(s => $"„{s.Name}“"));
        var text = running.Count == 1
            ? $"Der Server {names} läuft noch. Er wird sauber gestoppt und die Welt gespeichert, bevor der Launcher schließt."
            : $"Die Server {names} laufen noch. Sie werden sauber gestoppt und die Welten gespeichert, bevor der Launcher schließt.";
        if (!await _app.Dialogs.ConfirmAsync("Lokale Server stoppen?", text, "Stoppen und beenden", danger: true))
            return;
        try
        {
            await _app.Dialogs.RunWithProgressAsync("Server werden gestoppt …", async _ =>
            {
                await _app.LocalServers.StopAllAsync();
                return true;
            });
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Server beim Beenden stoppen", ex);
        }
        _closeConfirmed = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _app.Dispose();
        _tray?.Dispose();
        base.OnClosed(e);
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        foreach (var (nav, page) in _pages)
            Ui.Show(page, nav.IsChecked == true);

        if (sender == NavInstances)
            InstancesPage.ShowList();
        if (sender == NavSettings)
            SettingsPage.Initialize(_app);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        DialogHost.HandleKey(e);
        if (!e.Handled)
            base.OnPreviewKeyDown(e);
    }
}
