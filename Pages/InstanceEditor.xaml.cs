using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace McLauncher.Pages;

/// <summary>Formular zum Anlegen und Bearbeiten einer Instanz (Name, Loader, Version).</summary>
public partial class InstanceEditor : UserControl
{
    private AppState _app = null!;
    private Installation? _existing;
    private string _version = "";
    private int _loadRequest;
    private string? _pendingIcon; // gewähltes, noch nicht gespeichertes Bild
    private bool _iconReset;      // eigenes Bild beim Speichern entfernen

    /// <summary>Gespeichert: (Instanz, war neu).</summary>
    public event Action<Installation, bool>? Saved;
    public event Action<Installation>? Deleted;
    public event Action? Cancelled;

    public InstanceEditor()
    {
        InitializeComponent();
    }

    /// <summary>Füllt das Formular; <paramref name="existing"/> = null legt eine neue Instanz an.</summary>
    public void Edit(AppState app, Installation? existing)
    {
        _app = app;
        _existing = existing;
        _version = existing?.MinecraftVersion ?? "";
        VersionBox.ItemsSource = null; // Auswahl der vorher bearbeiteten Instanz verwerfen
        NameBox.Text = existing?.Name ?? "";
        _pendingIcon = null;
        _iconReset = false;
        AxoOnlyCheck.IsChecked = app.Settings.AxoVersionsOnly;
        SnapshotsCheck.IsChecked = app.Settings.ShowSnapshots;
        OldVersionsCheck.IsChecked = app.Settings.ShowOldVersions;
        DeleteButton.Visibility = existing != null && app.Settings.Installations.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Übertragen beim Anlegen direkt hier; bestehende Instanzen haben dafür einen eigenen Tab
        TransferSection.Visibility = existing == null ? Visibility.Visible : Visibility.Collapsed;
        if (existing == null)
            Transfer.Show(app, null);

        var radio = (existing?.Loader ?? LoaderType.Vanilla) switch
        {
            LoaderType.Fabric => FabricRadio,
            LoaderType.Forge => ForgeRadio,
            _ => VanillaRadio
        };
        if (radio.IsChecked == true)
            _ = LoadVersionsAsync(); // Checked feuert nicht erneut
        else
            radio.IsChecked = true;
        UpdateIconPreview();
    }

    // ---------- Bild ----------

    private bool HasCustomIcon =>
        _pendingIcon != null || (!_iconReset && _existing != null && InstanceIcons.CustomPath(_existing) != null);

    private void UpdateIconPreview()
    {
        var loader = SelectedLoader;
        IconLetter.Text = loader.ToString()[..1];
        IconDrop.Background = new System.Windows.Media.SolidColorBrush(loader switch
        {
            LoaderType.Fabric => System.Windows.Media.Color.FromRgb(0x8A, 0x6F, 0x4E),
            LoaderType.Forge => System.Windows.Media.Color.FromRgb(0x2E, 0x5A, 0x88),
            _ => System.Windows.Media.Color.FromRgb(0x3C, 0x3F, 0x45)
        });

        var worldIcon = _existing == null ? null : InstanceIcons.LoadLatestWorldIcon(_existing.GameDir);
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

    private void IconDrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        ChooseIcon_Click(sender, e);

    private void IconDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void IconDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            SetPendingIcon(files[0]);
    }

    private void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        _pendingIcon = null;
        _iconReset = true;
        UpdateIconPreview();
    }

    private LoaderType SelectedLoader =>
        FabricRadio.IsChecked == true ? LoaderType.Fabric
        : ForgeRadio.IsChecked == true ? LoaderType.Forge
        : LoaderType.Vanilla;

    private void Loader_Checked(object sender, RoutedEventArgs e)
    {
        if (_app == null)
            return;
        _ = LoadVersionsAsync();
        UpdateIconPreview(); // Farbe/Buchstabe der automatischen Kachel folgt dem Loader
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.AxoVersionsOnly = AxoOnlyCheck.IsChecked == true;
        _app.Settings.ShowSnapshots = SnapshotsCheck.IsChecked == true;
        _app.Settings.ShowOldVersions = OldVersionsCheck.IsChecked == true;
        _app.Save();
        _ = LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync()
    {
        var request = ++_loadRequest;
        var loader = SelectedLoader;
        // Forge veröffentlicht nur Vollversionen, Alpha/Beta gibt es nur ohne Mod-Loader
        SnapshotsCheck.IsEnabled = loader != LoaderType.Forge;
        OldVersionsCheck.IsEnabled = loader == LoaderType.Vanilla;
        // Die AxoClient-Mod gibt es nur für Fabric
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
                return; // inzwischen wurde etwas anderes gewählt

            if (axoOnly)
                versions = versions.Where(Badge.SupportedVersions.Contains).ToList();
            VersionBox.ItemsSource = versions;
            VersionBox.SelectedItem = versions.Contains(_version) ? _version : versions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (request != _loadRequest)
                return;
            // Offline: bisherige Version weiter anbieten
            VersionBox.ItemsSource = _version.Length > 0 ? new[] { _version } : Array.Empty<string>();
            VersionBox.SelectedItem = _version.Length > 0 ? _version : null;
            InfoText.Text = "Versionsliste konnte nicht geladen werden: " + ex.Message;
        }
        SaveButton.IsEnabled = VersionBox.SelectedItem != null;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (VersionBox.SelectedItem is not string version)
            return;

        var inst = _existing ?? new Installation();
        inst.Loader = SelectedLoader;
        inst.MinecraftVersion = version;
        inst.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? $"{inst.Loader} {version}" : NameBox.Text.Trim();

        var isNew = _existing == null;
        if (isNew)
        {
            // Eigener, eindeutiger Spielordner für Welten, Mods und Optionen
            var safeName = string.Concat(inst.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            inst.GameDir = Path.Combine(AppState.LauncherDir, "instances", $"{safeName}-{inst.Id}");
            _app.Settings.Installations.Add(inst);
        }

        try
        {
            if (_pendingIcon != null)
                inst.IconFile = InstanceIcons.Import(inst, _pendingIcon);
            else if (_iconReset)
            {
                InstanceIcons.Remove(inst);
                inst.IconFile = null;
            }
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowMessageAsync("Bild konnte nicht übernommen werden", ex.Message);
        }
        _pendingIcon = null;
        _iconReset = false;
        _app.NotifyInstallationsChanged();

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
        var confirmed = await _app.Dialogs.ConfirmAsync("Instanz löschen",
            $"Instanz \"{inst.Name}\" wirklich entfernen?\n\n" +
            $"Der Ordner mit Welten und Mods bleibt erhalten:\n{inst.GameDir}",
            "Löschen", danger: true);
        if (!confirmed)
            return;

        _app.Settings.Installations.Remove(inst);
        InstanceIcons.Remove(inst);
        if (_app.Settings.SelectedInstallationId == inst.Id)
            _app.Settings.SelectedInstallationId = _app.Settings.Installations[0].Id;
        _app.NotifyInstallationsChanged();
        Deleted?.Invoke(inst);
    }
}
