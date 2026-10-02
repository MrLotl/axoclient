using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.InstanceTabs;

public sealed class ShotRow : Observable
{
    private bool _editing;
    private bool _copied;

    public ShotRow(ScreenshotInfo shot)
    {
        Shot = shot;
        shot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScreenshotInfo.Thumbnail))
                Changed(nameof(Thumbnail));
        };
    }

    public ScreenshotInfo Shot { get; }
    public string Name => Path.GetFileNameWithoutExtension(Shot.FullPath);
    public string Meta => $"{Shot.Taken:HH:mm} · {Formats.Size(Shot.SizeBytes)}";
    public BitmapSource? Thumbnail => Shot.Thumbnail;
    public string Draft { get; set; } = "";

    public bool Editing
    {
        get => _editing;
        set
        {
            _editing = value;
            Changed(nameof(Editing), nameof(NotEditing));
        }
    }

    public bool NotEditing => !_editing;

    public bool Copied
    {
        get => _copied;
        set
        {
            _copied = value;
            Changed(nameof(CopyIcon));
        }
    }

    public string CopyIcon => _copied ? "Check" : "Copy";
}

public sealed record ShotGroup(string Title, List<ShotRow> Items)
{
    public string CountText => "· " + Items.Count;
}

public partial class ScreenshotsPanel : UserControl
{
    private AppServices _app = null!;
    private Installation? _inst;
    private ScreenshotStore _store = null!;
    private CancellationTokenSource? _thumbnails;
    private List<ScreenshotInfo> _shots = [];
    private int _loadRequest;
    private bool _ready;

    public ScreenshotsPanel()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation inst)
    {
        _ready = false;
        _app = app;
        _inst = inst;
        _store = new ScreenshotStore(inst);
        FilterBox.Text = "";
        ShowStatus("");
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
        if (_inst == null)
            return;
        var request = ++_loadRequest;
        _thumbnails?.Cancel();
        _thumbnails = new CancellationTokenSource();
        var cancel = _thumbnails.Token;
        SummaryText.Text = "Lädt …";
        var shots = await _store.LoadAsync();
        if (request != _loadRequest)
            return;
        _shots = shots;
        ApplyFilter();
        _ = ScreenshotStore.LoadThumbnailsAsync(shots, cancel);
    }

    private void ApplyFilter()
    {
        var query = FilterBox.Text.Trim();
        var shown = _shots.Where(s => query.Length == 0 || s.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        Groups.ItemsSource = shown
            .GroupBy(s => s.Taken.Date)
            .Select(g => new ShotGroup(Formats.Day(g.Key.ToUniversalTime().AddHours(12)), g.Select(s => new ShotRow(s)).ToList()))
            .ToList();
        SummaryText.Text = _shots.Count == 0 ? "" : $"{Formats.Count(_shots.Count, "Screenshot", "Screenshots")} · {Formats.Size(_shots.Sum(s => s.SizeBytes))}";
        EmptyText.Text = _shots.Count == 0 ? "Noch keine Screenshots. Drücke im Spiel F2." : "Keine Screenshots gefunden.";
        Ui.Show(EmptyText, shown.Count == 0);
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready)
            ApplyFilter();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<ShotRow>(sender);
        var image = Images.FromFile(row.Shot.FullPath);
        if (image == null)
        {
            Shell.OpenFile(row.Shot.FullPath);
            return;
        }
        var window = Window.GetWindow(this);
        var picture = new Border
        {
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            MaxHeight = (window?.ActualHeight ?? 800) - 200,
            Child = new Image { Source = image, Stretch = Stretch.Uniform }
        };
        var caption = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var open = DialogParts.Make("SmallButton", "Öffnen", () => Shell.OpenFile(row.Shot.FullPath), "External");
        DockPanel.SetDock(open, Dock.Right);
        caption.Children.Add(open);
        caption.Children.Add(new TextBlock
        {
            Text = row.Shot.FileName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ui.Resource<Brush>("TextStrong"),
            VerticalAlignment = VerticalAlignment.Center
        });
        caption.Children.Add(new TextBlock
        {
            Text = $"  {row.Shot.TakenText} · {Formats.Size(row.Shot.SizeBytes)}",
            FontSize = 13,
            Foreground = Ui.Resource<Brush>("MutedText"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = DialogParts.CloseButton(_app.Dialogs.ClosePanel);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 0, 0, 10);
        var root = new StackPanel { Margin = new Thickness(18), Children = { close, picture, caption } };
        await _app.Dialogs.ShowPanelAsync(root, Math.Min(1180, (window?.ActualWidth ?? 1200) - 80));
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(_store.Dir, create: true);

    private void ShowFile_Click(object sender, RoutedEventArgs e) => Shell.ShowFile(Ui.DataOf<ShotRow>(sender).Shot.FullPath);

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<ShotRow>(sender);
        row.Draft = row.Name;
        row.Editing = true;
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private async void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        var row = Ui.DataOf<ShotRow>(sender);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            row.Editing = false;
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await CommitAsync(row);
        }
    }

    private async void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<ShotRow>(sender);
        if (row.Editing)
            await CommitAsync(row);
    }

    private async Task CommitAsync(ShotRow row)
    {
        row.Editing = false;
        if (ScreenshotStore.CleanName(row.Draft) is not { Length: > 0 } clean || clean == row.Name)
            return;
        try
        {
            _store.Rename(row.Shot, clean);
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Umbenennen fehlgeschlagen", ex);
        }
        await RefreshAsync();
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<ShotRow>(sender);
        try
        {
            ScreenshotStore.CopyToClipboard(row.Shot);
            row.Copied = true;
            await Task.Delay(1500);
            row.Copied = false;
        }
        catch (Exception ex)
        {
            ShowStatus("Kopieren fehlgeschlagen: " + ErrorReport.Short(ex));
        }
    }

    private async void SetIcon_Click(object sender, RoutedEventArgs e) => await UseAsync(Ui.DataOf<ShotRow>(sender), banner: false);

    private async void SetBanner_Click(object sender, RoutedEventArgs e) => await UseAsync(Ui.DataOf<ShotRow>(sender), banner: true);

    private async Task UseAsync(ShotRow row, bool banner)
    {
        if (_inst is not { } inst)
            return;
        try
        {
            if (banner)
                InstanceIcons.SetBanner(inst, row.Shot.FullPath);
            else
                InstanceIcons.SetCustom(inst, row.Shot.FullPath);
            _app.Instances.NotifyChanged();
            ShowStatus(banner ? $"„{row.Shot.FileName}“ ist jetzt das Bannerbild." : $"„{row.Shot.FileName}“ ist jetzt das Instanzbild.");
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Bild konnte nicht übernommen werden", ex);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<ShotRow>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Screenshot löschen",
                $"„{row.Shot.FileName}“ in den Papierkorb verschieben?", "In den Papierkorb", danger: true))
            return;
        try
        {
            await Task.Run(() => FileOps.Recycle(row.Shot.FullPath));
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Löschen fehlgeschlagen", ex);
        }
        await RefreshAsync();
    }
}
