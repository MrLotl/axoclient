using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace McLauncher.Pages;

/// <summary>Welten einer Instanz: anzeigen, direkt starten, sichern, importieren, löschen.</summary>
public partial class WorldsPanel : UserControl
{
    private AppState _app = null!;
    private Installation _inst = null!;
    private WorldStore _store = null!;
    private int _loadRequest;

    /// <summary>Instanz soll direkt in eine Welt starten (Ordnername).</summary>
    public event Action<Installation, string>? PlayWorldRequested;

    public WorldsPanel()
    {
        InitializeComponent();
    }

    public void Show(AppState app, Installation inst)
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
            return; // inzwischen andere Instanz gewählt
        WorldList.ItemsSource = worlds;
        Header.Text = $"Welten ({worlds.Count})";
        EmptyText.Visibility = worlds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static WorldInfo WorldOf(object sender) => (WorldInfo)((FrameworkElement)sender).DataContext;

    private void Play_Click(object sender, RoutedEventArgs e) =>
        PlayWorldRequested?.Invoke(_inst, WorldOf(sender).FolderName);

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var world = WorldOf(sender);
        StatusText.Text = $"Sichere \"{world.DisplayName}\"...";
        try
        {
            var zip = await _store.BackupAsync(world);
            StatusText.Text = $"Backup erstellt: {Path.GetFileName(zip)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowMessageAsync("Backup fehlgeschlagen",
                ex.Message + "\n\nLäuft das Spiel noch mit dieser Welt? Dann zuerst das Spiel schließen.");
        }
    }

    private void OpenWorld_Click(object sender, RoutedEventArgs e) => OpenFolder(WorldOf(sender).FullPath);

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var world = WorldOf(sender);
        if (!await _app.Dialogs.ConfirmAsync("Welt löschen",
                $"\"{world.DisplayName}\" in den Papierkorb verschieben?\n\n" +
                "Tipp: Erstelle vorher ein Backup, falls du sie später noch brauchst.",
                "In den Papierkorb", danger: true))
            return;
        try
        {
            await Task.Run(() => WorldStore.MoveToRecycleBin(world));
            StatusText.Text = $"\"{world.DisplayName}\" wurde in den Papierkorb verschoben.";
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowMessageAsync("Löschen fehlgeschlagen", ex.Message);
        }
        await RefreshAsync();
    }

    // ---------- Import ----------

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Welt-Archiv (*.zip)|*.zip", Title = "Welt importieren" };
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
            var folder = await _store.ImportAsync(path);
            StatusText.Text = $"Welt \"{folder}\" importiert.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowMessageAsync("Import fehlgeschlagen", ex.Message);
        }
        await RefreshAsync();
    }

    private void OpenSaves_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_store.SavesDir);
        OpenFolder(_store.SavesDir);
    }

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        var dir = WorldStore.BackupDir(_inst);
        Directory.CreateDirectory(dir);
        OpenFolder(dir);
    }

    private static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
}
