using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AxoClient.UI.InstanceTabs;

public partial class InstanceEditor : UserControl
{
    private const string InheritedJava = "Wie in den Launcher-Einstellungen";

    private static readonly Dictionary<LoaderType, Color> LoaderColors = new()
    {
        [LoaderType.Vanilla] = Color.FromRgb(0x3C, 0x3F, 0x45),
        [LoaderType.Fabric] = Color.FromRgb(0x8A, 0x6F, 0x4E),
        [LoaderType.Forge] = Color.FromRgb(0x2E, 0x5A, 0x88)
    };

    private AppServices _app = null!;
    private Installation? _existing;
    private string _version = "";
    private int _loadRequest;
    private string? _pendingIcon;
    private bool _iconReset;
    private bool _loadingPerformance;

    public event Action<Installation, bool>? Saved;
    public event Action? Deleted;
    public event Action? Cancelled;

    public InstanceEditor()
    {
        InitializeComponent();
    }

    private Dictionary<LoaderType, RadioButton> LoaderRadios => new()
    {
        [LoaderType.Vanilla] = VanillaRadio,
        [LoaderType.Fabric] = FabricRadio,
        [LoaderType.Forge] = ForgeRadio
    };

    private LoaderType SelectedLoader => LoaderRadios.FirstOrDefault(r => r.Value.IsChecked == true).Key;

    public void Edit(AppServices app, Installation? existing)
    {
        _app = app;
        _existing = existing;
        _version = existing?.MinecraftVersion ?? "";
        VersionBox.ItemsSource = null;
        NameBox.Text = existing?.Name ?? "";
        _pendingIcon = null;
        _iconReset = false;
        AxoOnlyCheck.IsChecked = app.Settings.AxoVersionsOnly;
        SnapshotsCheck.IsChecked = app.Settings.ShowSnapshots;
        OldVersionsCheck.IsChecked = app.Settings.ShowOldVersions;
        Ui.Show(DeleteButton, existing != null && app.Instances.All.Count > 1);

        Ui.Show(TransferSection, existing == null);
        if (existing == null)
            Transfer.Show(app, null);

        Ui.Show(PerformanceSection, existing != null);
        if (existing != null)
            LoadPerformance(existing);

        var radio = LoaderRadios[existing?.Loader ?? LoaderType.Vanilla];
        if (radio.IsChecked == true)
            _ = LoadVersionsAsync();
        else
            radio.IsChecked = true;
        UpdateIconPreview();
    }

    private void LoadPerformance(Installation inst)
    {
        _loadingPerformance = true;
        InstanceRamSlider.Maximum = RamAdvisor.SliderMaximum();
        InstanceRamSlider.Value = Math.Clamp(RamAdvisor.CurrentMb(inst, _app.Settings),
            (int)InstanceRamSlider.Minimum, (int)InstanceRamSlider.Maximum);
        var advice = RamAdvisor.Recommend(inst);
        InstanceRamAdvice.Text = $"Empfohlen für diese Instanz: {Formats.Megabytes(advice.Mb)} ({advice.Reason}).";
        OwnRamCheck.IsChecked = inst.MaxRamMb != null;
        OwnJavaCheck.IsChecked = inst.JavaPath is { Length: > 0 };
        OwnJvmCheck.IsChecked = inst.JvmPreset != null || inst.JvmArguments != null;
        InstanceJvmArgsBox.Text = inst.JvmArguments ?? _app.Settings.JvmArguments ?? "";
        FillPresets(inst);
        _loadingPerformance = false;

        UpdateInstanceRamText();
        ApplyPerformanceEnabled();
        _ = LoadInstanceJavaAsync(inst);
    }

    private void FillPresets(Installation inst)
    {
        Ui.FillPresetChoices(InstancePresetButtons, "InstanceJvmPreset", JvmPresets.EffectiveFor(inst, _app.Settings).Id,
            InstancePreset_Checked);
        UpdateInstancePresetHint();
    }

    private void InstancePreset_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingPerformance || _existing == null || ((RadioButton)sender).Tag is not string id)
            return;
        _existing.JvmPreset = id;
        OwnJvmCheck.IsChecked = true;
        SavePerformance();
        UpdateInstancePresetHint();
        ApplyPerformanceEnabled();
    }

    private void UpdateInstancePresetHint() =>
        InstancePresetHint.Text = JvmPresets.EffectiveFor(_existing, _app.Settings).Description;

    private void OwnValue_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingPerformance || _existing is not { } inst)
            return;

        inst.MaxRamMb = OwnRamCheck.IsChecked == true ? (int)InstanceRamSlider.Value : null;
        inst.JavaPath = OwnJavaCheck.IsChecked == true ? (InstanceJavaBox.SelectedItem as JavaChoice)?.Runtime?.Path : null;

        if (OwnJvmCheck.IsChecked == true)
        {
            inst.JvmPreset = InstancePresetButtons.Children.OfType<RadioButton>()
                .FirstOrDefault(b => b.IsChecked == true)?.Tag as string ?? _app.Settings.JvmPreset;
            inst.JvmArguments = InstanceJvmArgsBox.Text.Trim();
        }
        else
        {
            inst.JvmPreset = null;
            inst.JvmArguments = null;
            InstanceJvmArgsBox.Text = _app.Settings.JvmArguments ?? "";
            _loadingPerformance = true;
            FillPresets(inst);
            _loadingPerformance = false;
        }

        SavePerformance();
        ApplyPerformanceEnabled();
    }

    private void ApplyPerformanceEnabled()
    {
        InstanceRamSlider.IsEnabled = OwnRamCheck.IsChecked == true;
        InstanceJavaBox.IsEnabled = OwnJavaCheck.IsChecked == true;
        InstancePresetButtons.IsEnabled = InstanceJvmArgsBox.IsEnabled = OwnJvmCheck.IsChecked == true;
    }

    private void UpdateInstanceRamText() => InstanceRamValue.Text = $"{InstanceRamSlider.Value / 1024:0.#} GB";

    private void InstanceRam_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateInstanceRamText();
        if (_loadingPerformance || _existing == null || OwnRamCheck.IsChecked != true)
            return;
        _existing.MaxRamMb = (int)InstanceRamSlider.Value;
        SavePerformance();
    }

    private void InstanceJvmArgs_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingPerformance || _existing is not { } inst || OwnJvmCheck.IsChecked != true)
            return;
        inst.JvmArguments = InstanceJvmArgsBox.Text.Trim();
        SavePerformance();
    }

    private async Task LoadInstanceJavaAsync(Installation inst)
    {
        InstanceJavaHint.Text = "Suche nach Java-Installationen...";
        List<JavaRuntime> found;
        try
        {
            found = await JavaChoice.FindAsync();
        }
        catch (Exception ex)
        {
            InstanceJavaHint.Text = "Die Java-Suche ist fehlgeschlagen: " + ErrorReport.Short(ex);
            return;
        }
        if (_existing != inst)
            return;

        var choices = JavaChoice.Build(InheritedJava, found, inst.JavaPath);
        _loadingPerformance = true;
        InstanceJavaBox.ItemsSource = choices;
        InstanceJavaBox.SelectedItem = JavaChoice.Find(choices, inst.JavaPath);
        _loadingPerformance = false;
        UpdateInstanceJavaHint(inst);
    }

    private void UpdateInstanceJavaHint(Installation inst)
    {
        var required = JavaRuntimes.RequiredMajor(inst);
        InstanceJavaHint.Text = inst.JavaPath is not { Length: > 0 } path
            ? $"Minecraft {inst.MinecraftVersion} braucht Java {required}. " +
              "Ohne eigene Angabe holt der Launcher die passende Laufzeit selbst."
            : File.Exists(path)
                ? $"Diese Instanz startet mit: {path}\nGebraucht wird Java {required}."
                : $"Achtung: \"{path}\" gibt es nicht (mehr). Nimm das Häkchen weg oder wähle ein anderes Java.";
    }

    private void InstanceJava_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPerformance || _existing is not { } inst || OwnJavaCheck.IsChecked != true)
            return;
        inst.JavaPath = (InstanceJavaBox.SelectedItem as JavaChoice)?.Runtime?.Path;
        SavePerformance();
        UpdateInstanceJavaHint(inst);
    }

    private void SavePerformance() => _app.Instances.NotifyChanged();

    private bool HasCustomIcon =>
        _pendingIcon != null || (!_iconReset && _existing != null && InstanceIcons.CustomPath(_existing) != null);

    private void UpdateIconPreview()
    {
        var loader = SelectedLoader;
        IconLetter.Text = loader.ToString()[..1];
        IconDrop.Background = new SolidColorBrush(LoaderColors[loader]);

        if (_pendingIcon != null)
        {
            IconPreview.Source = InstanceIcons.LoadImage(_pendingIcon);
            IconInfo.Text = "Eigenes Bild (wird beim Speichern übernommen).";
        }
        else if (HasCustomIcon)
        {
            IconPreview.Source = InstanceIcons.Load(_existing!);
            IconInfo.Text = "Eigenes Bild.";
        }
        else
        {
            var worldIcon = _existing == null ? null : InstanceIcons.LoadLatestWorldIcon(_existing);
            IconPreview.Source = worldIcon;
            IconInfo.Text = worldIcon != null
                ? "Automatisch: Vorschaubild der zuletzt gespielten Welt."
                : "Automatisch: Sobald du eine Welt gespielt hast, erscheint hier ihr Vorschaubild.";
        }
        ResetIconButton.IsEnabled = HasCustomIcon;
    }

    private void SetPendingIcon(string path)
    {
        if (InstanceIcons.LoadImage(path) == null)
        {
            _ = _app.Dialogs.ShowMessageAsync("Kein Bild", "Diese Datei kann nicht als Bild geöffnet werden.");
            return;
        }
        _pendingIcon = path;
        _iconReset = false;
        UpdateIconPreview();
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = InstanceIcons.FileFilter, Title = "Bild für die Instanz" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            SetPendingIcon(dialog.FileName);
    }

    private void IconDrop_Click(object sender, MouseButtonEventArgs e) => ChooseIcon_Click(sender, e);

    private void IconDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void IconDrop_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            SetPendingIcon(files[0]);
    }

    private void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        _pendingIcon = null;
        _iconReset = true;
        UpdateIconPreview();
    }

    private void Loader_Checked(object sender, RoutedEventArgs e)
    {
        if (_app == null)
            return;
        _ = LoadVersionsAsync();
        UpdateIconPreview();
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.AxoVersionsOnly = AxoOnlyCheck.IsChecked == true;
        _app.Settings.ShowSnapshots = SnapshotsCheck.IsChecked == true;
        _app.Settings.ShowOldVersions = OldVersionsCheck.IsChecked == true;
        _app.SaveSettings();
        _ = LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync()
    {
        var request = ++_loadRequest;
        var loader = SelectedLoader;
        SnapshotsCheck.IsEnabled = loader != LoaderType.Forge;
        OldVersionsCheck.IsEnabled = loader == LoaderType.Vanilla;
        AxoOnlyCheck.IsEnabled = loader == LoaderType.Fabric;
        var axoOnly = AxoOnlyCheck.IsChecked == true && AxoOnlyCheck.IsEnabled;
        InfoText.Text = loader == LoaderType.Vanilla
            ? ""
            : "Mods, Ressourcenpakete und Shader verwaltest du danach in den Tabs dieser Instanz.";

        if (VersionBox.SelectedItem is string current)
            _version = current;
        SaveButton.IsEnabled = false;
        try
        {
            var versions = await VersionCatalog.GetVersionsAsync(_app.Http, loader,
                SnapshotsCheck.IsChecked == true, OldVersionsCheck.IsChecked == true);
            if (request != _loadRequest)
                return;
            if (axoOnly)
                versions = versions.Where(BadgeMod.SupportedVersions.Contains).ToList();
            VersionBox.ItemsSource = versions;
            VersionBox.SelectedItem = versions.Contains(_version) ? _version : versions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (request != _loadRequest)
                return;
            VersionBox.ItemsSource = _version.Length > 0 ? new[] { _version } : Array.Empty<string>();
            VersionBox.SelectedItem = _version.Length > 0 ? _version : null;
            InfoText.Text = "Versionsliste konnte nicht geladen werden: " + ErrorReport.Short(ex);
        }
        SaveButton.IsEnabled = VersionBox.SelectedItem != null;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (VersionBox.SelectedItem is not string version)
            return;

        var isNew = _existing == null;
        var inst = _existing ?? new Installation();
        inst.Loader = SelectedLoader;
        inst.MinecraftVersion = version;
        inst.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? $"{inst.Loader} {version}" : NameBox.Text.Trim();

        try
        {
            if (_pendingIcon != null)
                InstanceIcons.SetCustom(inst, _pendingIcon);
            else if (_iconReset)
                InstanceIcons.Remove(inst);
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Bild konnte nicht übernommen werden", ex);
        }
        _pendingIcon = null;
        _iconReset = false;

        if (isNew)
            _app.Instances.Add(inst);
        else
            _app.Instances.NotifyChanged();

        if (isNew && Transfer.HasSelection)
        {
            SaveButton.IsEnabled = CancelButton.IsEnabled = false;
            await Transfer.RunAsync(inst);
            SaveButton.IsEnabled = CancelButton.IsEnabled = true;
        }
        Saved?.Invoke(inst, isNew);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancelled?.Invoke();

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is not { } inst)
            return;
        if (!await _app.Dialogs.ConfirmAsync("Instanz löschen",
                $"Instanz \"{inst.Name}\" wirklich entfernen?\n\nDer Ordner mit Welten und Mods bleibt erhalten:\n{inst.GameDir}",
                "Löschen", danger: true))
            return;
        _app.Instances.Remove(inst);
        Deleted?.Invoke();
    }
}
