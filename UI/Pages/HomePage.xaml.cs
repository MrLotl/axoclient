using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CmlLib.Core;
using CmlLib.Core.Installers;

namespace AxoClient.UI.Pages;

public partial class HomePage : UserControl
{
    private AppServices _app = null!;
    private NotificationCenter _notifications = null!;
    private bool _busy;
    private double _percent;
    private string _stage = "";

    public HomePage()
    {
        InitializeComponent();
        PlayLayers.SizeChanged += (_, _) =>
            PlayLayers.Clip = new RectangleGeometry(new Rect(PlayLayers.RenderSize), 12, 12);
        IsVisibleChanged += (_, _) => CloseInstanceMenu();
        SizeChanged += (_, _) => CloseInstanceMenu();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && InstanceMenu.IsVisible)
            {
                CloseInstanceMenu();
                e.Handled = true;
            }
        };
    }

    public event Action<Installation, InstanceSection?>? OpenInstanceRequested;

    public NotificationCenter Notifications => _notifications;

    public void Initialize(AppServices app)
    {
        _app = app;
        _notifications = new NotificationCenter(app);
        _app.Accounts.Changed += UpdateAccount;
        _app.Instances.Changed += RefreshInstallations;
        _app.Games.Changed += () => Dispatcher.InvokeAsync(UpdateControls);
        Friends.Initialize(app, this);
        Servers.Initialize(app, this);
        RefreshInstallations();
        UpdateAccount();
    }

    public void ShowStatus(string text)
    {
        StatusText.Text = text;
        Ui.Show(StatusText, text.Length > 0);
    }

    public void RefreshFriends() => Friends.Refresh();

    public void OpenInstance(Installation inst, InstanceSection? section) => OpenInstanceRequested?.Invoke(inst, section);

    public Task ShowNotificationsAsync() => NotificationsDialog.ShowAsync(_app, _notifications, this);

    private void RefreshInstallations()
    {
        var items = _app.Instances.All.Select(i => new InstanceItem(i, _app.Games.IsRunning(i))).ToList();
        InstanceList.ItemsSource = items;
        var selected = items.FirstOrDefault(i => i.Installation == _app.Instances.Selected);
        CurrentThumb.Content = selected;
        CurrentName.Text = selected?.Name ?? "Keine Instanz";
        CurrentVersion.Text = selected != null ? "· " + selected.VersionLabel : "";
        UpdateControls();
        Servers.Refresh();
    }

    private void InstancePicker_Checked(object sender, RoutedEventArgs e)
    {
        var top = LaunchPanel.TranslatePoint(new Point(0, 0), this).Y;
        InstanceMenu.Width = LaunchPanel.ActualWidth;
        InstanceMenu.Margin = new Thickness(0, 0, 0, ActualHeight - top + 8);
        InstanceList.MaxHeight = Math.Clamp(top - 70, 120, 360);
        InstanceDismiss.Visibility = Visibility.Visible;
        InstanceMenu.Visibility = Visibility.Visible;
        InstanceMenu.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        ((RotateTransform)PickerChevron.RenderTransform).Angle = 180;
    }

    private void CloseInstanceMenu()
    {
        if (InstanceMenu.Visibility != Visibility.Visible)
            return;
        InstanceMenu.Visibility = Visibility.Collapsed;
        InstanceDismiss.Visibility = Visibility.Collapsed;
        ((RotateTransform)PickerChevron.RenderTransform).Angle = 0;
        InstancePicker.IsChecked = false;
    }

    private void InstanceDismiss_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CloseInstanceMenu();
        e.Handled = true;
    }

    private void InstanceItem_Click(object sender, RoutedEventArgs e)
    {
        CloseInstanceMenu();
        _app.Instances.Select(Ui.DataOf<InstanceItem>(sender).Installation);
    }

    private void SkinArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var height = Math.Max(0, Math.Min(e.NewSize.Height, e.NewSize.Width * 1.5));
        SkinView.Height = height;
        SkinView.Width = height / 1.5;
    }

    private void UpdateAccount()
    {
        SkinView.SetSkin(_app.Accounts.Profile?.SkinPng, _app.Accounts.Profile?.SkinSlim ?? false, _app.Capes.DisplayPng);
        UpdateControls();
        Friends.Refresh();
    }

    private void UpdateControls()
    {
        var running = _app.Instances.Selected is { } inst && _app.Games.IsRunning(inst);
        InstancePicker.IsEnabled = !_busy;
        if (_busy)
            return;

        PlayFill.Width = 0;
        PlayShine.Visibility = Visibility.Collapsed;
        PlayPercent.Visibility = Visibility.Collapsed;
        PlayIcon.Visibility = Visibility.Visible;
        PlayButton.IsEnabled = running || (_app.Accounts.Session != null && _app.Instances.Selected != null);
        if (running)
        {
            PlayButton.Tag = "flat";
            PlayButton.Background = Ui.Frozen(Color.FromRgb(0x3B, 0x26, 0x26));
            PlayButton.BorderBrush = Ui.Frozen(Color.FromArgb(0x59, 0xF4, 0x70, 0x67));
            PlayButton.BorderThickness = new Thickness(1);
            PlayIcon.Kind = "Stop";
            PlayIcon.Foreground = PlayText.Foreground = Ui.Frozen(Color.FromRgb(0xFF, 0x8A, 0x80));
            PlayText.Text = "Beenden";
            PlayButton.ToolTip = "Minecraft beenden";
        }
        else
        {
            PlayButton.Tag = null;
            PlayButton.Background = Ui.Resource<Brush>("Accent");
            PlayButton.BorderThickness = new Thickness(0);
            PlayIcon.Kind = "Play";
            PlayIcon.Foreground = PlayText.Foreground = Brushes.White;
            PlayText.Text = "Spielen";
            PlayButton.ToolTip = _app.Accounts.Session == null ? "Bitte oben rechts anmelden" : null;
        }
        PlayText.FontSize = 20;
    }

    private void ShowLaunching()
    {
        PlayButton.Tag = "flat";
        PlayButton.Background = Ui.Frozen(Color.FromRgb(0x2E, 0x25, 0x33));
        PlayButton.BorderBrush = Ui.Resource<Brush>("AccentLine");
        PlayButton.BorderThickness = new Thickness(1);
        PlayIcon.Visibility = Visibility.Collapsed;
        PlayText.Foreground = Brushes.White;
        PlayText.FontSize = 15;
        PlayText.FontWeight = FontWeights.SemiBold;
        PlayPercent.Visibility = Visibility.Visible;
        PlayShine.Visibility = Visibility.Visible;
        var sweep = new DoubleAnimation(-120, 460, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever };
        ShineMove.BeginAnimation(TranslateTransform.XProperty, sweep);
        SetLaunchProgress(0, "Prüfe Dateien …");
    }

    private void SetLaunchProgress(double percent, string? stage = null)
    {
        _percent = Math.Clamp(Math.Max(percent, _percent), 0, 100);
        if (stage != null)
            _stage = stage;
        PlayText.Text = _stage;
        PlayPercent.Text = $"{Math.Round(_percent)} %";
        PlayFill.Width = PlayLayers.ActualWidth * _percent / 100;
    }

    private void EndLaunching()
    {
        ShineMove.BeginAnimation(TranslateTransform.XProperty, null);
        PlayText.FontWeight = FontWeights.Bold;
        _percent = 0;
        UpdateControls();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
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
                $"„{inst.Name}“ läuft schon. Beende es zuerst, um es neu zu starten.");
            return;
        }

        _busy = true;
        _percent = 0;
        InstancePicker.IsEnabled = false;
        ShowStatus("");
        ShowLaunching();
        var status = new Progress<string>(text => SetLaunchProgress(_percent, Stage(text)));
        var progress = new LaunchProgress(
            status,
            new Progress<InstallerProgressChangedEventArgs>(args =>
            {
                if (args.TotalTasks > 0)
                    SetLaunchProgress(10 + 60.0 * args.ProgressedTasks / args.TotalTasks,
                        args.TotalTasks > 1 ? $"Lade Dateien ({args.ProgressedTasks}/{args.TotalTasks}) …" : null);
            }),
            new Progress<ByteProgress>(args =>
            {
                if (args.TotalBytes > 0)
                    SetLaunchProgress(10 + 60.0 * args.ProgressedBytes / args.TotalBytes);
            }));

        try
        {
            if (!await PreLaunchDialog.ConfirmAsync(_app, inst, status))
            {
                ShowStatus("Start abgebrochen.");
                return;
            }

            SetLaunchProgress(Math.Max(_percent, 8), "Lade Bibliotheken …");
            await _app.Games.LaunchAsync(inst, quickPlay, progress);
            SetLaunchProgress(100, "Viel Spaß!");
            await Task.Delay(1600);
            if (quickPlay?.Server is { } server)
                ShowStatus($"{inst.Name} startet und verbindet mit {server}.");
            else if (quickPlay?.World is { } world)
                ShowStatus($"{inst.Name} startet die Welt „{world}“.");
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Start abgebrochen.");
        }
        catch (Exception ex)
        {
            ShowStatus("Start fehlgeschlagen: " + ErrorReport.Short(ex));
            await _app.Dialogs.ShowErrorAsync("Start fehlgeschlagen", ex);
        }
        finally
        {
            _busy = false;
            EndLaunching();
        }
    }

    private static string Stage(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("java"))
            return "Lade Java …";
        if (lower.Contains("mod"))
            return "Prüfe Mods …";
        if (lower.Contains("start"))
            return "Starte Minecraft …";
        return text.Length > 34 ? text[..33].TrimEnd('.', ' ') + " …" : text;
    }

    public async Task JoinServerAsync(string server, string? version, string? friendName, bool ask)
    {
        if (_busy)
            return;
        if (_app.Accounts.Session == null)
        {
            await _app.Dialogs.ShowMessageAsync("Nicht angemeldet", "Bitte melde dich oben rechts an, um beizutreten.");
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
              $"Es wird „{inst.Name}“ ({inst.MinecraftVersion}) verwendet."
            : "";
        if ((ask || mismatch.Length > 0)
            && !await _app.Dialogs.ConfirmAsync("Server beitreten", $"Mit „{inst.Name}“ auf {server} beitreten?{mismatch}",
                "Beitreten"))
            return;

        _app.Instances.Select(inst);
        await PlayAsync(new QuickPlay(Server: server));
    }
}
