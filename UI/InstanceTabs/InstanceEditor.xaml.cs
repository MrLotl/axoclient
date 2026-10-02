using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.InstanceTabs;

public partial class InstanceEditor : UserControl
{
    private AppServices _app = null!;
    private Installation? _existing;
    private string _version = "";
    private int _loadRequest;
    private string? _pendingIcon;
    private bool _iconReset;
    private string? _pendingBanner;
    private bool _bannerReset;
    private bool _bannerFromIcon;
    private bool _loading;
    private bool _confirmDelete;
    private bool _creating;

    public event Action<Installation, bool>? Saved;
    public event Action? Deleted;
    public event Action? Cancelled;
    public event Action<Installation>? Upgraded;
    public event Action? ModpacksRequested;

    public InstanceEditor()
    {
        InitializeComponent();
        foreach (var source in new[] { SourceEmpty, SourceCopy, SourceImport, SourcePack })
            source.Content = SourceContent((string)source.Tag);
    }

    private static UIElement SourceContent(string tag)
    {
        var parts = tag.Split('|');
        var tile = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(9),
            Background = Ui.Resource<Brush>("AccentSoft"),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Icon { Kind = parts[0], Size = 16, Foreground = Ui.Resource<Brush>("AccentText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = parts[1], FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong") });
        texts.Children.Add(new TextBlock { Text = parts[2], FontSize = 12, Foreground = Ui.Resource<Brush>("MutedText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var dock = new DockPanel();
        DockPanel.SetDock(tile, Dock.Left);
        dock.Children.Add(tile);
        dock.Children.Add(texts);
        return dock;
    }

    private Dictionary<LoaderType, RadioButton> LoaderRadios => new()
    {
        [LoaderType.Vanilla] = VanillaRadio,
        [LoaderType.Fabric] = FabricRadio,
        [LoaderType.Forge] = ForgeRadio,
        [LoaderType.NeoForge] = NeoForgeRadio,
        [LoaderType.Quilt] = QuiltRadio
    };

    private LoaderType SelectedLoader => LoaderRadios.FirstOrDefault(r => r.Value.IsChecked == true).Key;

    private bool IsNew => _existing == null;

    public void Edit(AppServices app, Installation? existing)
    {
        _loading = true;
        _app = app;
        _existing = existing;
        _version = existing?.MinecraftVersion ?? "";
        _pendingIcon = _pendingBanner = null;
        _iconReset = _bannerReset = false;
        _bannerFromIcon = existing?.BannerFromIcon ?? false;
        _confirmDelete = false;
        _creating = false;
        VersionBox.ItemsSource = null;
        NameBox.Text = existing?.Name ?? "";
        AxoOnlyCheck.IsChecked = app.Settings.AxoVersionsOnly;
        SnapshotsCheck.IsChecked = app.Settings.ShowSnapshots;
        OldVersionsCheck.IsChecked = app.Settings.ShowOldVersions;

        Ui.Show(CreateHeader, IsNew);
        Ui.Show(SourceSection, IsNew);
        Ui.Show(ManageSection, !IsNew);
        Ui.Show(DeleteButton, !IsNew && app.Instances.All.Count > 1);
        Ui.Show(SummaryText, IsNew);
        Ui.Show(CreateProgress, false);
        CancelButton.IsEnabled = SaveButton.IsEnabled = true;
        SaveIcon.Visibility = IsNew ? Visibility.Visible : Visibility.Collapsed;
        SaveText.Text = IsNew ? "Instanz erstellen" : "Speichern";
        DeleteText.Text = "Instanz löschen";
        DeleteButton.Style = Ui.Resource<Style>("DangerButton");
        SourceEmpty.IsChecked = true;
        if (IsNew)
            Transfer.Show(app, null);

        var ramMb = existing != null ? RamAdvisor.CurrentMb(existing, app.Settings) : app.Settings.MaxRamMb;
        RamSlider.Maximum = Math.Max(4, Math.Min(32, RamAdvisor.TotalMb() / 1024));
        RamMax.Text = $"{RamSlider.Maximum:0} GB";
        RamSlider.Value = Math.Clamp(Math.Round(ramMb / 1024.0), 1, RamSlider.Maximum);
        JvmArgsBox.Text = existing != null
            ? JvmPresets.Resolve(app.Settings, existing)
            : JvmPresets.Get(JvmPresets.BalancedId).Arguments;
        UpdateRam();

        var radio = LoaderRadios[existing?.Loader ?? LoaderType.Fabric];
        _loading = false;
        if (radio.IsChecked == true)
            _ = LoadVersionsAsync();
        else
            radio.IsChecked = true;
        UpdateImages();
        SetStatus("", false);
        UpdateSummary();
    }

    private void SetStatus(string text, bool good)
    {
        StatusText.Text = text;
        StatusText.Foreground = Ui.Resource<Brush>(good ? "Good" : "Warn");
    }

    private void MarkDirty()
    {
        if (_loading || IsNew)
            return;
        SetStatus("Ungespeicherte Änderungen", false);
        ResetDeleteConfirm();
    }

    private void ResetDeleteConfirm()
    {
        if (!_confirmDelete)
            return;
        _confirmDelete = false;
        DeleteText.Text = "Instanz löschen";
        DeleteButton.Style = Ui.Resource<Style>("DangerButton");
    }

    private void Dirty_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        MarkDirty();
        UpdateImages();
        UpdateSummary();
    }

    private void UpdateRam()
    {
        var gb = (int)RamSlider.Value;
        RamValue.Text = $"{gb} GB";
        RamHint.Text = gb < 4 ? "Zu wenig für Mods" : gb > 10 ? "Mehr als nötig" : "Empfohlen: 4–8 GB";
    }

    private void Ram_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RamValue == null)
            return;
        UpdateRam();
        MarkDirty();
    }

    private string DisplayName => string.IsNullOrWhiteSpace(NameBox.Text)
        ? IsNew ? "Neue Instanz" : _existing!.Name
        : NameBox.Text.Trim();

    private BitmapSource? CurrentIcon()
    {
        if (_pendingIcon != null)
            return InstanceIcons.LoadImage(_pendingIcon);
        if (!_iconReset && _existing != null)
            return InstanceIcons.Load(_existing);
        return _existing != null ? InstanceIcons.LoadLatestWorldIcon(_existing) : null;
    }

    private BitmapSource? CurrentBanner(BitmapSource? icon)
    {
        if (_bannerFromIcon)
            return icon;
        if (_pendingBanner != null)
            return InstanceIcons.LoadImage(_pendingBanner);
        if (!_bannerReset && _existing != null && InstanceIcons.BannerPath(_existing) is { } path)
            return Images.FromFile(path, 1200);
        return icon;
    }

    private bool BannerIsAuto => _bannerFromIcon
                                 || (_pendingBanner == null && (_bannerReset || _existing == null || InstanceIcons.BannerPath(_existing) == null));

    private void UpdateImages()
    {
        if (_app == null)
            return;
        var placeholder = _existing != null ? InstanceText.Placeholder(_existing) : InstanceText.Placeholder(new Installation { Id = "new" });
        var icon = CurrentIcon();
        IconPreview.Background = icon != null ? new ImageBrush(icon) { Stretch = Stretch.UniformToFill } : placeholder;
        var banner = CurrentBanner(icon);
        BannerPreview.Background = banner != null
            ? new ImageBrush(banner) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center }
            : placeholder;
        BannerName.Text = DisplayName;
        Ui.Show(BannerAutoBadge, BannerIsAuto);
        IconInfo.Text = _pendingIcon != null ? "Eigenes Bild (wird beim Speichern übernommen)"
            : icon == null ? "Für Karten und Liste · ideal 16:9" : "Für Karten und Liste · ideal 16:9";
        PreviewThumb.Background = IconPreview.Background;
        PreviewName.Text = DisplayName;
    }

    private void UpdateSummary()
    {
        PreviewVersion.Text = VersionBox.SelectedItem is string v
            ? SelectedLoader == LoaderType.Vanilla ? $"{v} · Vanilla" : $"{v} · {SelectedLoader}"
            : "";
        if (!IsNew)
            return;
        var source = SourceCopy.IsChecked == true ? " · übernimmt Daten einer anderen Instanz" : "";
        SummaryText.Text = VersionBox.SelectedItem is string version
            ? $"{DisplayName} · Minecraft {version} · {SelectedLoader} · {(int)RamSlider.Value} GB{source}"
            : "Wähle eine Minecraft-Version.";
    }

    private string? PickImage(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = InstanceIcons.FileFilter, Title = title };
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }

    private bool IsImage(string path)
    {
        if (InstanceIcons.LoadImage(path) != null)
            return true;
        _ = _app.Dialogs.ShowMessageAsync("Kein Bild", "Diese Datei kann nicht als Bild geöffnet werden.");
        return false;
    }

    private void SetIcon(string path)
    {
        if (!IsImage(path))
            return;
        _pendingIcon = path;
        _iconReset = false;
        MarkDirty();
        UpdateImages();
    }

    private void SetBanner(string path)
    {
        if (!IsImage(path))
            return;
        _pendingBanner = path;
        _bannerReset = false;
        _bannerFromIcon = false;
        MarkDirty();
        UpdateImages();
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (PickImage("Instanzbild wählen") is { } path)
            SetIcon(path);
    }

    private void IconPreview_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ChooseIcon_Click(sender, e);

    private void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        _pendingIcon = null;
        _iconReset = true;
        MarkDirty();
        UpdateImages();
    }

    private void ChooseBanner_Click(object sender, RoutedEventArgs e)
    {
        if (PickImage("Bannerbild wählen") is { } path)
            SetBanner(path);
    }

    private void AutoBanner_Click(object sender, RoutedEventArgs e)
    {
        _bannerFromIcon = true;
        _pendingBanner = null;
        MarkDirty();
        UpdateImages();
    }

    private void ResetBanner_Click(object sender, RoutedEventArgs e)
    {
        _pendingBanner = null;
        _bannerReset = true;
        _bannerFromIcon = false;
        MarkDirty();
        UpdateImages();
    }

    private void Image_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Icon_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            SetIcon(files[0]);
    }

    private void Banner_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            SetBanner(files[0]);
    }

    private void Loader_Checked(object sender, RoutedEventArgs e)
    {
        if (_app == null || _loading)
            return;
        MarkDirty();
        _ = LoadVersionsAsync();
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.AxoVersionsOnly = AxoOnlyCheck.IsChecked == true;
        _app.Settings.ShowSnapshots = SnapshotsCheck.IsChecked == true;
        _app.Settings.ShowOldVersions = OldVersionsCheck.IsChecked == true;
        _app.SaveSettings();
        _ = LoadVersionsAsync();
    }

    private void Version_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionBox.SelectedItem is string version)
        {
            Ui.Show(IncompatibleText, !BadgeMod.SupportedVersions.Contains(version));
            if (!_loading && version != _version)
                MarkDirty();
        }
        UpdateSummary();
    }

    private async Task LoadVersionsAsync()
    {
        var request = ++_loadRequest;
        var loader = SelectedLoader;
        SnapshotsCheck.IsEnabled = loader is LoaderType.Vanilla or LoaderType.Fabric or LoaderType.Quilt;
        OldVersionsCheck.IsEnabled = loader == LoaderType.Vanilla;
        AxoOnlyCheck.IsEnabled = loader.IsFabricLike();
        var axoOnly = AxoOnlyCheck.IsChecked == true && AxoOnlyCheck.IsEnabled;
        InfoText.Text = "Lade Versionen …";

        if (VersionBox.SelectedItem is string current)
            _version = current;
        SaveButton.IsEnabled = false;
        try
        {
            var versions = await VersionCatalog.GetVersionsAsync(_app.Http, loader,
                SnapshotsCheck.IsChecked == true && SnapshotsCheck.IsEnabled, OldVersionsCheck.IsChecked == true && OldVersionsCheck.IsEnabled);
            if (request != _loadRequest)
                return;
            if (axoOnly)
                versions = versions.Where(BadgeMod.SupportedVersions.Contains).ToList();
            _loading = true;
            VersionBox.ItemsSource = versions;
            VersionBox.SelectedItem = versions.Contains(_version) ? _version : versions.FirstOrDefault();
            _loading = false;
            InfoText.Text = loader == LoaderType.Vanilla ? "" : "Mods, Ressourcepacks und Shader verwaltest du danach in den Tabs dieser Instanz.";
        }
        catch (Exception ex)
        {
            if (request != _loadRequest)
                return;
            _loading = true;
            VersionBox.ItemsSource = _version.Length > 0 ? new[] { _version } : Array.Empty<string>();
            VersionBox.SelectedItem = _version.Length > 0 ? _version : null;
            _loading = false;
            InfoText.Text = "Versionsliste konnte nicht geladen werden: " + ErrorReport.Short(ex);
        }
        SaveButton.IsEnabled = VersionBox.SelectedItem != null && !_creating;
        if (VersionBox.SelectedItem is string selected)
            Ui.Show(IncompatibleText, !BadgeMod.SupportedVersions.Contains(selected));
        UpdateSummary();
    }

    private void Source_Checked(object sender, RoutedEventArgs e)
    {
        if (CopyPanel == null)
            return;
        Ui.Show(CopyPanel, SourceCopy.IsChecked == true);
        Ui.Show(ImportPanel, SourceImport.IsChecked == true);
        Ui.Show(ForeignPanel, SourceImport.IsChecked == true);
        Ui.Show(PackPanel, SourcePack.IsChecked == true);
        SaveButton.Visibility = SourceImport.IsChecked == true || SourcePack.IsChecked == true
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateSummary();
    }

    private async void ImportFile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Modpacks und Pakete (*.mrpack;*.zip;*.json)|*.mrpack;*.zip;*.json",
            Title = "Instanz importieren"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            await ImportAsync(dialog.FileName);
    }

    private async void ImportFile_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await ImportAsync(files[0]);
    }

    private async Task ImportAsync(string path)
    {
        await DropHandler.HandleAsync(_app, [path]);
        Cancelled?.Invoke();
    }

    private async void Foreign_Click(object sender, RoutedEventArgs e)
    {
        await ForeignImportDialog.RunAsync(_app);
        Cancelled?.Invoke();
    }

    private void Modpacks_Click(object sender, RoutedEventArgs e) => ModpacksRequested?.Invoke();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (VersionBox.SelectedItem is not string version || _creating)
            return;

        var isNew = IsNew;
        var inst = _existing ?? new Installation();
        inst.Loader = SelectedLoader;
        inst.MinecraftVersion = version;
        inst.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? $"{inst.Loader} {version}" : NameBox.Text.Trim();
        inst.MaxRamMb = (int)RamSlider.Value * 1024;
        inst.JvmPreset = JvmPresets.CustomId;
        inst.JvmArguments = JvmArgsBox.Text.Trim();

        try
        {
            if (_pendingIcon != null)
                InstanceIcons.SetCustom(inst, _pendingIcon);
            else if (_iconReset)
                InstanceIcons.Remove(inst);
            if (_pendingBanner != null)
                InstanceIcons.SetBanner(inst, _pendingBanner);
            else if (_bannerReset)
                InstanceIcons.RemoveBanner(inst);
            inst.BannerFromIcon = _bannerFromIcon;
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Bild konnte nicht übernommen werden", ex);
        }
        _pendingIcon = _pendingBanner = null;
        _iconReset = _bannerReset = false;

        if (isNew)
        {
            _creating = true;
            SaveButton.IsEnabled = CancelButton.IsEnabled = false;
            Ui.Show(CreateProgress, true);
            CreateStage.Text = "Lege Instanz an …";
            CreatePercent.Text = "";
            _app.Instances.Add(inst);
            if (SourceCopy.IsChecked == true && Transfer.HasSelection)
            {
                CreateStage.Text = "Übernehme Daten …";
                await Transfer.RunAsync(inst);
            }
            CreateBar.IsIndeterminate = false;
            CreateBar.Value = 100;
            CreatePercent.Text = "100 %";
            CreateStage.Text = "Fertig";
            _creating = false;
        }
        else
        {
            _app.Instances.NotifyChanged();
            SetStatus("Gespeichert", true);
        }
        Saved?.Invoke(inst, isNew);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (IsNew)
        {
            Cancelled?.Invoke();
            return;
        }
        Edit(_app, _existing);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is not { } inst)
            return;
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            DeleteText.Text = "Wirklich löschen?";
            DeleteButton.Style = Ui.Resource<Style>("DangerFillButton");
            DeleteButton.ToolTip = $"Entfernt „{inst.Name}“ aus dem Launcher. Der Ordner mit Welten und Mods bleibt erhalten.";
            return;
        }
        _app.Instances.Remove(inst);
        Deleted?.Invoke();
    }

    private async void Backups_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is not { } inst)
            return;
        var panel = new BackupPanel { Margin = new Thickness(22, 16, 22, 22) };
        panel.Show(_app, inst);
        await ShowPanelAsync("Sicherungen", inst, "Archive", panel, 720);
    }

    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is not { } inst)
            return;
        var panel = new TransferPanel { Margin = new Thickness(22, 16, 22, 22) };
        panel.Show(_app, inst);
        await ShowPanelAsync("Daten übernehmen", inst, "Swap", panel, 600);
    }

    private async Task ShowPanelAsync(string title, Installation inst, string icon, FrameworkElement panel, double width)
    {
        var header = DialogParts.Header(title, $"{inst.Name} · {InstanceText.Version(inst)}", DialogParts.IconTile(icon),
            _app.Dialogs.ClosePanel);
        header.Margin = new Thickness(22, 22, 22, 0);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(panel);
        await _app.Dialogs.ShowPanelAsync(root, width);
    }

    private async void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        if (_existing != null && await UpgradeDialog.RunAsync(_app, _existing) is { } upgraded)
            Upgraded?.Invoke(upgraded);
    }
}
