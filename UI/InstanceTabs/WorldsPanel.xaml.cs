using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.InstanceTabs;

public partial class WorldsPanel : UserControl
{
    private AppServices _app = null!;
    private Installation _inst = null!;
    private WorldStore _store = null!;
    private List<WorldInfo> _worlds = [];
    private int _loadRequest;
    private bool _ready;

    public event Action<Installation, string>? PlayWorldRequested;

    public WorldsPanel()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation inst)
    {
        _ready = false;
        _app = app;
        _inst = inst;
        _store = new WorldStore(inst);
        FilterBox.Text = "";
        ShowStatus("");
        (app.Settings.ContentAsTiles ? GridToggle : ListToggle).IsChecked = true;
        ApplyViewMode();
        _ready = true;
        _ = RefreshAsync();
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        Ui.Show(StatusText, text.Length > 0);
    }

    private async Task RefreshAsync()
    {
        var request = ++_loadRequest;
        SummaryText.Text = "Lädt …";
        var worlds = await _store.LoadAsync();
        if (request != _loadRequest)
            return;
        _worlds = worlds;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = FilterBox.Text.Trim();
        var shown = _worlds.Where(w => query.Length == 0 || w.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        WorldList.ItemsSource = shown;
        SummaryText.Text = $"{Formats.Count(_worlds.Count, "Welt", "Welten")} · {Formats.Size(_worlds.Sum(w => w.SizeBytes))}";
        EmptyText.Text = _worlds.Count == 0 ? "Noch keine Welten. Starte das Spiel oder importiere eine Welt (.zip)." : "Keine Welten gefunden.";
        Ui.Show(EmptyText, shown.Count == 0);
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready)
            ApplyFilter();
    }

    private void ViewToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _app.Settings.ContentAsTiles = GridToggle.IsChecked == true;
        _app.SaveSettings();
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        var grid = GridToggle.IsChecked == true;
        WorldList.ItemTemplate = (DataTemplate)FindResource(grid ? "CardTemplate" : "ListTemplate");
        WorldList.ItemsPanel = (ItemsPanelTemplate)FindResource(grid ? "GridPanel" : "ListPanel");
    }

    private void Play_Click(object sender, RoutedEventArgs e) =>
        PlayWorldRequested?.Invoke(_inst, Ui.DataOf<WorldInfo>(sender).FolderName);

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var world = Ui.DataOf<WorldInfo>(sender);
        ShowStatus($"Sichere \"{world.DisplayName}\"...");
        try
        {
            var zip = await _store.BackupAsync(world);
            ShowStatus($"Backup erstellt: {Path.GetFileName(zip)}");
        }
        catch (Exception ex)
        {
            ShowStatus("");
            await _app.Dialogs.ShowErrorAsync("Backup fehlgeschlagen", ex,
                "Läuft das Spiel noch mit dieser Welt? Dann zuerst das Spiel schließen.");
        }
    }

    private void OpenWorld_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(Ui.DataOf<WorldInfo>(sender).FullPath);

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var world = Ui.DataOf<WorldInfo>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Welt löschen",
                $"\"{world.DisplayName}\" in den Papierkorb verschieben?\n\n" +
                "Tipp: Erstelle vorher ein Backup, falls du sie später noch brauchst.",
                "In den Papierkorb", danger: true))
            return;
        try
        {
            await Task.Run(() => FileOps.Recycle(world.FullPath));
            ShowStatus($"\"{world.DisplayName}\" wurde in den Papierkorb verschoben.");
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Löschen fehlgeschlagen", ex);
        }
        await RefreshAsync();
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Welt-Archiv (*.zip)|*.zip", Title = "Welt importieren" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            await ImportAsync(dialog.FileName);
    }

    private void Panel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Panel_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;
        foreach (var path in paths)
            await ImportAsync(path);
    }

    private async Task ImportAsync(string path)
    {
        ShowStatus($"Importiere {Path.GetFileName(path)}...");
        try
        {
            ShowStatus($"Welt \"{await _store.ImportAsync(path)}\" importiert.");
        }
        catch (Exception ex)
        {
            ShowStatus("");
            await _app.Dialogs.ShowErrorAsync("Import fehlgeschlagen", ex);
        }
        await RefreshAsync();
    }

    private void OpenSaves_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(_store.SavesDir, create: true);

    private void OpenBackups_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(_inst.BackupDir, create: true);
}
