using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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

    public MainWindow()
    {
        InitializeComponent();
        _app = new AppServices(DialogHost);
        _pages = new Dictionary<RadioButton, UserControl>
        {
            [NavHome] = HomePage,
            [NavInstances] = InstancesPage,
            [NavSkins] = SkinsPage,
            [NavGameLog] = GameLogPage,
            [NavSettings] = SettingsPage
        };

        Account.Initialize(_app);
        HomePage.Initialize(_app);
        InstancesPage.Initialize(_app);
        SkinsPage.Initialize(_app);
        GameLogPage.Initialize(_app);
        SettingsPage.Initialize(_app);

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
        if (Autostart.StartedByWindows && _app.Settings.AutostartMinimized)
            WindowState = WindowState.Minimized;
    }

    public IDialogService Dialogs => DialogHost;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = "Version " + AppInfo.Version + (Updater.IsEnabled ? "" : " (lokal)");
        Updater.CleanUp();

        if (LauncherSettings.LoadProblem is { } problem)
            await Dialogs.ShowErrorAsync("Einstellungen konnten nicht gelesen werden", problem.Error, problem.Text);

        var updateCheck = CheckForUpdateAsync(manual: false);
        await Account.RestoreSessionAsync();
        _sessionRestored.TrySetResult();
        await updateCheck;
    }

    private async void VersionText_Click(object sender, MouseButtonEventArgs e) => await CheckForUpdateAsync(manual: true);

    private Task CheckForUpdateAsync(bool manual) =>
        UpdateDialog.CheckAsync(_app, manual, status =>
        {
            NavHome.IsChecked = true;
            HomePage.ShowStatus(status);
        }, Close);

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
        if (await Dialogs.ConfirmAsync("Minecraft ist abgestürzt",
                $"\"{inst.Name}\" wurde mit einem Fehler beendet. Soll der Launcher die Ursache suchen und Maßnahmen vorschlagen?",
                "Analysieren"))
            await CrashDialog.ShowAsync(_app, inst, justCrashed: true);
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

    private void BringToFront()
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
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Verkleinern" : "Maximieren";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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
