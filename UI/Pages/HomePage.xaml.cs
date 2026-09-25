using System.Windows;
using System.Windows.Controls;
using CmlLib.Core;
using CmlLib.Core.Installers;

namespace AxoClient.UI.Pages;

public partial class HomePage : UserControl
{
    private AppServices _app = null!;
    private bool _busy;
    private bool _refreshing;

    public HomePage()
    {
        InitializeComponent();
    }

    public void Initialize(AppServices app)
    {
        _app = app;
        _app.Accounts.Changed += UpdateAccount;
        _app.Instances.Changed += RefreshInstallations;
        _app.Games.Changed += UpdateControls;
        Friends.Initialize(app, this);
        Servers.Initialize(app, this);
        RefreshInstallations();
        UpdateAccount();
    }

    public void ShowStatus(string text) => StatusText.Text = text;

    private void RefreshInstallations()
    {
        _refreshing = true;
        InstallationBox.ItemsSource = null;
        InstallationBox.ItemsSource = _app.Instances.All;
        InstallationBox.SelectedItem = _app.Instances.Selected;
        _refreshing = false;
        UpdateControls();
        Servers.Refresh();
    }

    private void InstallationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && InstallationBox.SelectedItem is Installation inst)
            _app.Instances.Select(inst);
    }

    private void UpdateAccount()
    {
        PlayerName.Text = _app.Accounts.Session?.Username ?? "Nicht angemeldet";
        SkinView.SetSkin(_app.Accounts.Profile?.SkinPng, _app.Accounts.Profile?.SkinSlim ?? false, _app.Capes.DisplayPng);
        if (!_busy)
            StatusText.Text = _app.Accounts.Session == null ? "Bitte links unten anmelden." : "Bereit";
        UpdateControls();
        Friends.Refresh();
    }

    private void UpdateControls()
    {
        var running = _app.Instances.Selected is { } inst && _app.Games.IsRunning(inst);
        PlayButtons.Apply(PlayButton, running);
        PlayButton.IsEnabled = !_busy && (running || (_app.Accounts.Session != null && _app.Instances.Selected != null));
        InstallationBox.IsEnabled = !_busy;
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Instances.Selected is { } inst)
            await PlayButtons.PlayOrStopAsync(_app, inst, () => _ = PlayAsync());
    }

    public async Task PlayAsync(QuickPlay? quickPlay = null)
    {
        if (_busy || _app.Accounts.Session == null || _app.Instances.Selected is not { } inst)
            return;
        if (_app.Games.IsRunning(inst))
        {
            await _app.Dialogs.ShowMessageAsync("Läuft bereits",
                $"\"{inst.Name}\" läuft schon. Beende es zuerst, um es neu zu starten.");
            return;
        }

        _busy = true;
        UpdateControls();
        var status = new Progress<string>(text => StatusText.Text = text);
        var progress = new LaunchProgress(
            status,
            new Progress<InstallerProgressChangedEventArgs>(args =>
                StatusText.Text = $"[{args.ProgressedTasks}/{args.TotalTasks}] {args.Name}"),
            new Progress<ByteProgress>(args =>
            {
                if (args.TotalBytes > 0)
                    Progress.Value = args.ProgressedBytes * 100.0 / args.TotalBytes;
            }));

        try
        {
            if (!await PreLaunchDialog.ConfirmAsync(_app, inst, status))
            {
                StatusText.Text = "Start abgebrochen.";
                return;
            }

            await _app.Games.LaunchAsync(inst, quickPlay, progress);
            Progress.Value = 100;
            StatusText.Text = quickPlay?.Server is { } server ? $"{inst.Name} startet und verbindet mit {server}."
                : quickPlay?.World is { } world ? $"{inst.Name} startet die Welt \"{world}\"."
                : $"{inst.Name} wurde gestartet.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Start abgebrochen.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Start fehlgeschlagen: " + ErrorReport.Short(ex);
            await _app.Dialogs.ShowErrorAsync("Start fehlgeschlagen", ex);
        }
        finally
        {
            _busy = false;
            UpdateControls();
        }
    }

    public async Task JoinServerAsync(string server, string? version, string? friendName, bool ask)
    {
        if (_busy)
            return;
        if (_app.Accounts.Session == null)
        {
            await _app.Dialogs.ShowMessageAsync("Nicht angemeldet", "Bitte melde dich links unten an, um beizutreten.");
            return;
        }

        var inst = _app.Instances.Selected is { } selected && (version == null || selected.MinecraftVersion == version)
            ? selected
            : _app.Instances.All.Where(i => i.MinecraftVersion == version)
                  .OrderByDescending(i => BadgeMod.IsActiveFor(i, _app.Settings))
                  .FirstOrDefault()
              ?? _app.Instances.Selected;
        if (inst == null)
            return;

        var who = friendName != null ? $"{friendName} spielt" : "Dort wird";
        var mismatch = version != null && inst.MinecraftVersion != version
            ? $"\n\nAchtung: {who} mit Minecraft {version} gespielt, du hast keine Instanz mit dieser Version. " +
              $"Es wird \"{inst.Name}\" ({inst.MinecraftVersion}) verwendet."
            : "";
        if ((ask || mismatch.Length > 0)
            && !await _app.Dialogs.ConfirmAsync("Server beitreten", $"Mit \"{inst.Name}\" auf {server} beitreten?{mismatch}",
                "Beitreten"))
            return;

        _app.Instances.Select(inst);
        await PlayAsync(new QuickPlay(Server: server));
    }
}
