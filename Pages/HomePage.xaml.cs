using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CmlLib.Core;
using CmlLib.Core.Installers;

namespace McLauncher.Pages;

public partial class HomePage : UserControl
{
    private AppState _app = null!;
    private bool _busy;
    private bool _refreshing;
    private bool _loadingFriends;
    private readonly DispatcherTimer _friendsTimer = new() { Interval = TimeSpan.FromSeconds(20) };

    public HomePage()
    {
        InitializeComponent();
    }

    public void Initialize(AppState app)
    {
        _app = app;
        _app.AccountChanged += UpdateAccount;
        _app.InstallationsChanged += RefreshInstallations;
        _app.RunningGamesChanged += UpdateControls;
        RefreshInstallations();
        UpdateAccount();

        // Freundesliste regelmäßig aktualisieren, solange die Startseite sichtbar ist
        _friendsTimer.Tick += (_, _) => _ = RefreshFriendsAsync();
        _friendsTimer.Start();
        IsVisibleChanged += (_, _) => _ = RefreshFriendsAsync();
    }

    private void RefreshInstallations()
    {
        _refreshing = true;
        InstallationBox.ItemsSource = null;
        InstallationBox.ItemsSource = _app.Settings.Installations;
        InstallationBox.SelectedItem = _app.SelectedInstallation;
        _refreshing = false;
        UpdateControls();
    }

    private void InstallationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && InstallationBox.SelectedItem is Installation inst)
            _app.SelectInstallation(inst);
    }

    private void UpdateAccount()
    {
        PlayerName.Text = _app.Session?.Username ?? "Nicht angemeldet";
        SkinView.SetSkin(_app.Profile?.SkinPng, _app.Profile?.SkinSlim ?? false);
        if (!_busy)
            StatusText.Text = _app.Session == null ? "Bitte links unten anmelden." : "Bereit";
        UpdateControls();
        _ = RefreshFriendsAsync();
    }

    private void UpdateControls()
    {
        var running = _app.SelectedInstallation is { } inst && _app.IsRunning(inst);
        PlayButtons.Apply(PlayButton, running);
        PlayButton.IsEnabled = !_busy && (running || (_app.Session != null && _app.SelectedInstallation != null));
        InstallationBox.IsEnabled = !_busy;
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_app.SelectedInstallation is { } inst && _app.IsRunning(inst))
            _ = _app.StopGameAsync(inst);
        else
            _ = PlayAsync();
    }

    /// <summary>Installiert (falls nötig) und startet die ausgewählte Instanz, optional direkt in Welt/Server.</summary>
    public async Task PlayAsync(QuickPlay? quickPlay = null)
    {
        if (_busy || _app.Session == null || _app.SelectedInstallation is not { } inst)
            return;
        if (_app.IsRunning(inst))
        {
            await _app.Dialogs.ShowMessageAsync("Läuft bereits",
                $"\"{inst.Name}\" läuft schon. Beende es zuerst, um es neu zu starten.");
            return;
        }

        _busy = true;
        UpdateControls();

        // Progress<T> meldet automatisch zurück auf den UI-Thread
        var status = new Progress<string>(text => StatusText.Text = text);
        var fileProgress = new Progress<InstallerProgressChangedEventArgs>(args =>
            StatusText.Text = $"[{args.ProgressedTasks}/{args.TotalTasks}] {args.Name}");
        var byteProgress = new Progress<ByteProgress>(args =>
        {
            if (args.TotalBytes > 0)
                Progress.Value = args.ProgressedBytes * 100.0 / args.TotalBytes;
        });

        try
        {
            // Token auffrischen, falls der Launcher lange offen war
            var session = await _app.GetFreshSessionAsync();
            GameStatusWatcher.Reset(inst);
            var process = await _app.Installer.PrepareAsync(inst, session, _app.Settings, status, fileProgress,
                byteProgress, quickPlay);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _app.Discord.GameExited();
            process.Start();
            _app.Discord.GameStarted(_app.Settings, inst, quickPlay);
            GameStatusWatcher.Watch(_app, inst, quickPlay, process);
            _app.TrackGame(inst, process);
            _ = RegisterQuietlyAsync();
            Progress.Value = 100;
            StatusText.Text = quickPlay?.Server is { } server ? $"{inst.Name} startet und verbindet mit {server}."
                : quickPlay?.World is { } world ? $"{inst.Name} startet die Welt \"{world}\"."
                : $"{inst.Name} wurde gestartet.";
            if (_app.Settings.MinimizeOnLaunch)
                Window.GetWindow(this)!.WindowState = WindowState.Minimized;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler: " + ex.Message;
            await _app.Dialogs.ShowMessageAsync("Start fehlgeschlagen", ex.Message);
        }
        finally
        {
            _busy = false;
            UpdateControls();
        }
    }
}
