using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AxoClient.UI.InstanceTabs;

public partial class ScreenshotsPanel : UserControl
{
    private AppServices _app = null!;
    private Installation? _inst;
    private ScreenshotStore _store = null!;
    private CancellationTokenSource? _thumbnails;
    private int _loadRequest;

    public ScreenshotsPanel()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation inst)
    {
        _app = app;
        _inst = inst;
        _store = new ScreenshotStore(inst);
        StatusText.Text = "";
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_inst == null)
            return;
        var request = ++_loadRequest;
        _thumbnails?.Cancel();
        _thumbnails = new CancellationTokenSource();
        var cancel = _thumbnails.Token;

        Header.Text = "Screenshots (lädt...)";
        var shots = await _store.LoadAsync();
        if (request != _loadRequest)
            return;

        ShotList.ItemsSource = shots;
        Header.Text = $"Screenshots ({shots.Count})";
        SubHeader.Text = shots.Count == 0
            ? "Mit F2 im Spiel aufgenommen"
            : $"Belegen zusammen {Formats.Size(shots.Sum(s => s.SizeBytes))}";
        Ui.Show(EmptyText, shots.Count == 0);
        _ = ScreenshotStore.LoadThumbnailsAsync(shots, cancel);
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void Open_Click(object sender, MouseButtonEventArgs e) => Shell.OpenFile(Ui.DataOf<ScreenshotInfo>(sender).FullPath);

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(_store.Dir, create: true);

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var shot = Ui.DataOf<ScreenshotInfo>(sender);
        var box = Ui.Input(Path.GetFileNameWithoutExtension(shot.FullPath));
        var form = Ui.Stack(Ui.Label("Neuer Name (ohne Endung)"), box,
            Ui.Note($"Die Endung {Path.GetExtension(shot.FullPath)} bleibt erhalten.", 0, 11));
        if (!await _app.Dialogs.ShowFormAsync("Screenshot umbenennen", form, "Umbenennen",
                () => ScreenshotStore.CleanName(box.Text).Length > 0))
            return;
        try
        {
            StatusText.Text = $"Umbenannt in \"{Path.GetFileName(_store.Rename(shot, box.Text))}\".";
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Umbenennen fehlgeschlagen", ex);
        }
        await RefreshAsync();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var shot = Ui.DataOf<ScreenshotInfo>(sender);
        try
        {
            ScreenshotStore.CopyToClipboard(shot);
            StatusText.Text = $"\"{shot.FileName}\" liegt in der Zwischenablage.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Kopieren fehlgeschlagen: " + ErrorReport.Short(ex);
        }
    }

    private async void SetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_inst is not { } inst)
            return;
        var shot = Ui.DataOf<ScreenshotInfo>(sender);
        try
        {
            InstanceIcons.SetCustom(inst, shot.FullPath);
            _app.Instances.NotifyChanged();
            StatusText.Text = $"\"{shot.FileName}\" ist jetzt das Bild der Instanz.";
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Bild konnte nicht übernommen werden", ex);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var shot = Ui.DataOf<ScreenshotInfo>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Screenshot löschen",
                $"\"{shot.FileName}\" in den Papierkorb verschieben?", "In den Papierkorb", danger: true))
            return;
        try
        {
            await Task.Run(() => FileOps.Recycle(shot.FullPath));
            StatusText.Text = $"\"{shot.FileName}\" wurde in den Papierkorb verschoben.";
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Löschen fehlgeschlagen", ex);
        }
        await RefreshAsync();
    }
}
