using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace AxoClient.UI;

public sealed class ImportPick(ForeignLauncher launcher) : INotifyPropertyChanged
{
    private bool _chosen = true;

    public ForeignLauncher Launcher { get; } = launcher;
    public string Name => Launcher.Name;
    public List<ForeignInstance> Usable { get; } = launcher.Instances.Where(i => i.Importable && i.Complete).ToList();

    public string Detail
    {
        get
        {
            var mods = Usable.Count(i => i.Mods > 0);
            var worlds = Usable.Sum(i => i.Worlds);
            var parts = new List<string> { Formats.Count(Usable.Count, "Instanz", "Instanzen") };
            if (mods > 0)
                parts.Add(mods == Usable.Count ? "mit Mods" : $"{mods} mit Mods");
            if (worlds > 0)
                parts.Add(Formats.Count(worlds, "Welt", "Welten"));
            return string.Join(" · ", parts);
        }
    }

    public bool Chosen
    {
        get => _chosen;
        set
        {
            _chosen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Chosen)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class AccountRow(SavedAccount account, string state)
{
    private static readonly Brush OkBrush = Ui.Frozen(Color.FromRgb(0x3F, 0xB9, 0x50));

    public SavedAccount Account { get; } = account;
    public string Name => Account.Name;
    public BitmapSource? Head => Account.Head;
    public bool Busy => state == "busy";
    public bool Ok => state == "ok";
    public bool Idle => state == "idle";
    public bool Enabled { get; init; } = true;

    public string Sub => state switch
    {
        "busy" => "Warte auf Anmeldung …",
        "ok" => "Angemeldet",
        _ => Account.LastUsed == default
            ? "Abgemeldet"
            : "Abgemeldet · zuletzt " + Formats.Ago(Account.LastUsed.ToUniversalTime())
    };

    public Brush SubBrush => state switch
    {
        "busy" => Ui.Resource<Brush>("AccentText"),
        "ok" => OkBrush,
        _ => Ui.Resource<Brush>("MutedText")
    };

    public Brush RowBackground => state switch
    {
        "ok" => Ui.Frozen(Color.FromArgb(0x14, 0x3F, 0xB9, 0x50)),
        "busy" => Ui.Frozen(Color.FromRgb(0x33, 0x2C, 0x38)),
        _ => Ui.Frozen(Color.FromRgb(0x2C, 0x2C, 0x2C))
    };

    public Brush RowBorder => state switch
    {
        "ok" => Ui.Frozen(Color.FromArgb(0x4D, 0x3F, 0xB9, 0x50)),
        "busy" => Ui.Frozen(Color.FromArgb(0x4D, 0xD6, 0x8C, 0xE6)),
        _ => Brushes.Transparent
    };
}

public partial class WelcomeView : UserControl
{
    private AppServices _app = null!;
    private Task<List<ForeignLauncher>>? _scan;
    private List<ImportPick> _picks = [];
    private string? _busyId;
    private string? _okId;
    private bool _working;

    public WelcomeView()
    {
        InitializeComponent();
        VersionText.Text = "v" + AppInfo.ShortVersion;
    }

    public event Action? Finished;

    public void ShowFirstStart(AppServices app)
    {
        _app = app;
        Visibility = Visibility.Visible;
        Opacity = 1;
        ShowStep(StepWelcome);
        ((Storyboard)Resources["Float"]).Begin(HeroLogo, true);
        _scan = ForeignLaunchers.ScanAsync(WorkProgress.Silent);
    }

    public void ShowWelcomeBack(AppServices app)
    {
        _app = app;
        Visibility = Visibility.Visible;
        Opacity = 1;
        _busyId = _okId = null;
        ShowStep(StepBack);
        var instances = app.Instances.All;
        InstanceStack.ItemsSource = instances.Take(6).Select((inst, i) => InstanceTile(inst, i)).ToList();
        InstanceSummary.Text = Formats.Count(instances.Count, "Instanz", "Instanzen") + " gefunden";
        RefreshAccounts();
    }

    private static FrameworkElement InstanceTile(Installation inst, int index)
    {
        var image = Controls.InstanceText.Placeholder(inst);
        var tile = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Background = image,
            BorderBrush = Ui.Frozen(Color.FromRgb(0x26, 0x26, 0x26)), BorderThickness = new Thickness(2),
            Margin = new Thickness(index == 0 ? 0 : -10, 0, 0, 0)
        };
        if (InstanceIcons.Load(inst) is { } icon)
            tile.Child = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new ImageBrush(icon) { Stretch = Stretch.UniformToFill }
            };
        return tile;
    }

    private void ShowStep(FrameworkElement step)
    {
        foreach (var s in new FrameworkElement[] { StepWelcome, StepLogin, StepSetup, StepDone, StepBack })
            s.Visibility = s == step ? Visibility.Visible : Visibility.Collapsed;
        step.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        UpdateDots(step);
    }

    private void UpdateDots(FrameworkElement step)
    {
        Dots.Children.Clear();
        var steps = new FrameworkElement[] { StepWelcome, StepLogin, StepSetup, StepDone };
        var current = Array.IndexOf(steps, step);
        if (current < 0)
            return;
        for (var i = 0; i < steps.Length; i++)
            Dots.Children.Add(new Border
            {
                Height = 6, Width = i == current ? 22 : 6, CornerRadius = new CornerRadius(3),
                Margin = new Thickness(4, 0, 4, 0),
                Background = i <= current ? Ui.Resource<Brush>("AccentHorizontal") : Ui.Frozen(Color.FromRgb(0x3A, 0x3A, 0x3A))
            });
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        LoginProblem.Visibility = Visibility.Collapsed;
        ShowStep(StepLogin);
        try
        {
            await _app.Accounts.LoginAsync();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Anmeldung beim ersten Start", ex);
            ShowStep(StepWelcome);
            LoginProblem.Text = "Die Anmeldung wurde abgebrochen oder ist fehlgeschlagen. " + ErrorReport.Short(ex);
            LoginProblem.Visibility = Visibility.Visible;
            return;
        }
        await ShowSetupAsync();
    }

    private async Task ShowSetupAsync()
    {
        var name = _app.Accounts.Session?.Username ?? "";
        HelloText.Text = name.Length > 0 ? $"Hallo {name}!" : "Hallo!";
        SetupHead.ImageSource = _app.Accounts.Profile?.Head;

        var totalGb = Math.Max(2, RamAdvisor.TotalMb() / 1024);
        RamSlider.Maximum = Math.Clamp(totalGb - 2, 2, 32);
        var recommended = totalGb >= 16 ? 6 : totalGb >= 12 ? 4 : 3;
        RamSlider.Value = Math.Min(recommended, RamSlider.Maximum);
        RamHint.Text = $"Dein PC hat {totalGb} GB. Für Mods empfehlen wir {Math.Min(recommended, (int)RamSlider.Maximum)} GB.";
        UpdateRam();
        ShowStep(StepSetup);

        ImportNote.Text = "Suche nach anderen Launchern auf deinem PC …";
        ImportList.ItemsSource = null;
        try
        {
            var launchers = _scan != null ? await _scan : [];
            _picks = launchers.Where(l => l.Installed).Select(l => new ImportPick(l)).Where(p => p.Usable.Count > 0).ToList();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Andere Launcher suchen", ex);
            _picks = [];
        }
        foreach (var pick in _picks)
            pick.Chosen = pick.Launcher.Name == "Offizieller Launcher";
        ImportList.ItemsSource = _picks;
        ImportNote.Text = _picks.Count > 0
            ? "Wir haben auf deinem PC gefunden:"
            : "Keine anderen Launcher gefunden – das geht später jederzeit unter Instanzen → Importieren.";
    }

    private void Ram_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RamValue != null)
            UpdateRam();
    }

    private void UpdateRam() => RamValue.Text = $"{(int)RamSlider.Value} GB";

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_working)
            return;
        _working = true;
        FinishButton.IsEnabled = false;
        _app.Settings.MaxRamMb = (int)RamSlider.Value * 1024;
        _app.SaveSettings();

        var choices = _picks.Where(p => p.Chosen)
            .SelectMany(p => p.Usable)
            .Select(i => new ForeignImportChoice(i, i.Name, ForeignInstance.AsLoaderType(i.Loader)!.Value, i.MinecraftVersion!))
            .ToList();
        var imported = 0;
        if (choices.Count > 0)
        {
            ImportStatus.Visibility = Visibility.Visible;
            FinishText.Text = "Übernehme …";
            try
            {
                var progress = new WorkProgress(new Progress<string>(text => ImportStatus.Text = text),
                    new Progress<double>(), CancellationToken.None);
                await new ForeignImporter(_app).ImportAsync(choices, progress);
                imported = choices.Count;
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Import beim ersten Start", ex);
                ImportStatus.Text = "Nicht alles ließ sich übernehmen: " + ErrorReport.Short(ex);
            }
        }

        var first = _app.Instances.Selected ?? _app.Instances.All.FirstOrDefault();
        DoneText.Text = imported > 0
            ? $"{Formats.Count(imported, "Installation wurde", "Installationen wurden")} übernommen. Viel Spaß beim Spielen!"
            : first != null
                ? $"Deine erste Instanz „{first.Name}“ wartet schon auf dich."
                : "Viel Spaß beim Spielen!";
        _working = false;
        ShowStep(StepDone);
    }

    private void RefreshAccounts()
    {
        AccountList.ItemsSource = _app.Accounts.Saved
            .Select(a => new AccountRow(a, a.Id == _busyId ? "busy" : a.Id == _okId ? "ok" : "idle") { Enabled = _busyId == null })
            .ToList();
        OtherButton.IsEnabled = _busyId == null;
        var done = _okId != null;
        ContinueButton.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        BackHint.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Account_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<AccountRow>(sender);
        if (row.Ok)
        {
            Close();
            return;
        }
        await SignInAsync(row.Account.Id, () => _app.Accounts.SwitchAsync(row.Account.Id));
    }

    private async void Other_Click(object sender, RoutedEventArgs e) =>
        await SignInAsync("", () => _app.Accounts.LoginAsync());

    private async Task SignInAsync(string id, Func<Task> signIn)
    {
        _busyId = id;
        _okId = null;
        BackProblem.Visibility = Visibility.Collapsed;
        RefreshAccounts();
        try
        {
            await signIn();
            _busyId = null;
            _okId = _app.Accounts.Saved.FirstOrDefault(a => a.Active)?.Id ?? id;
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Anmeldung (Willkommen zurück)", ex);
            _busyId = null;
            BackProblem.Text = "Die Anmeldung wurde abgebrochen oder ist fehlgeschlagen. " + ErrorReport.Short(ex);
            BackProblem.Visibility = Visibility.Visible;
        }
        RefreshAccounts();
    }

    private void Close_Welcome_Click(object sender, RoutedEventArgs e) => Close();

    private void Close()
    {
        if (!_app.Settings.SetupDone)
        {
            _app.Settings.SetupDone = true;
            _app.SaveSettings();
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            BeginAnimation(OpacityProperty, null);
            ((Storyboard)Resources["Float"]).Stop(HeroLogo);
            Finished?.Invoke();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this)!.WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this)!;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this)!.Close();
}
