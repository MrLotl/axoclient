using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.InstanceTabs;

public partial class WorldsPanel : UserControl
{
    private AppServices _app = null!;
    private Installation _inst = null!;
    private WorldStore _store = null!;
    private int _loadRequest;

    public event Action<Installation, string>? PlayWorldRequested;

    public WorldsPanel()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation inst)
    {
        _app = app;
        _inst = inst;
        _store = new WorldStore(inst);
        StatusText.Text = "";
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var request = ++_loadRequest;
        Header.Text = "Welten (lädt...)";
        var worlds = await _store.LoadAsync();
        if (request != _loadRequest)
            return;
        WorldList.ItemsSource = worlds;
        Header.Text = $"Welten ({worlds.Count})";
        Ui.Show(EmptyText, worlds.Count == 0);
    }

    private void Play_Click(object sender, RoutedEventArgs e) =>
        PlayWorldRequested?.Invoke(_inst, Ui.DataOf<WorldInfo>(sender).FolderName);

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var world = Ui.DataOf<WorldInfo>(sender);
        StatusText.Text = $"Sichere \"{world.DisplayName}\"...";
        try
        {
            var zip = await _store.BackupAsync(world);
            StatusText.Text = $"Backup erstellt: {Path.GetFileName(zip)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
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
            StatusText.Text = $"\"{world.DisplayName}\" wurde in den Papierkorb verschoben.";
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
        StatusText.Text = $"Importiere {Path.GetFileName(path)}...";
        try
        {
            StatusText.Text = $"Welt \"{await _store.ImportAsync(path)}\" importiert.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowErrorAsync("Import fehlgeschlagen", ex);
        }
        await RefreshAsync();
    }

    private void OpenSaves_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(_store.SavesDir, create: true);

    private void OpenBackups_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(_inst.BackupDir, create: true);
}
