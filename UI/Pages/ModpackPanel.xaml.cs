using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AxoClient.UI.Pages;

public sealed class PackRow : Observable
{
    private string _state = "idle";
    private string _stage = "";
    private double _percent;
    private bool _indeterminate = true;

    public PackRow(ContentProject project)
    {
        Project = project;
        var url = project.ImageUrl ?? project.IconUrl;
        if (url != null)
        {
            try
            {
                Image = new BitmapImage(new Uri(url)) { DecodePixelWidth = 360 };
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Modpack-Bild laden", ex);
            }
        }
        Placeholder = Letters.BrushFor(project.Title);
    }

    public ContentProject Project { get; }
    public ImageSource? Image { get; }
    public Brush Placeholder { get; }
    public bool HasTags => Project.Tags.Length > 0;
    public string LatestLabel { get; set; } = "Neueste Version";
    public string InstallTip => $"{Project.Title} als neue Instanz installieren";
    public Installation? Installed { get; set; }
    public string BusyVersion { get; set; } = "";
    public string DoneVersion { get; set; } = "";

    public bool Idle => _state == "idle";
    public bool Busy => _state == "busy";
    public bool Done => _state == "done";

    public string State
    {
        get => _state;
        set
        {
            _state = value;
            Changed(nameof(Idle), nameof(Busy), nameof(Done), nameof(BusyVersion), nameof(DoneVersion));
        }
    }

    public string Stage
    {
        get => _stage;
        set
        {
            _stage = value;
            Changed();
        }
    }

    public double Percent
    {
        get => _percent;
        set
        {
            _percent = value;
            Indeterminate = false;
            Changed(nameof(Percent), nameof(PercentText));
        }
    }

    public bool Indeterminate
    {
        get => _indeterminate;
        set
        {
            _indeterminate = value;
            Changed();
        }
    }

    public string PercentText => Indeterminate ? "" : $"{Math.Round(_percent)} %";
}

public partial class ModpackPanel : UserControl
{
    private static readonly (string Label, string? Value)[] Loaders =
        [("Alle Loader", null), ("Fabric", "fabric"), ("Forge", "forge"), ("NeoForge", "neoforge"), ("Quilt", "quilt")];

    private static readonly (string Label, string? Value)[] Versions =
        [("Alle Versionen", null), ("1.21.x", "1.21.1"), ("1.20.x", "1.20.1"), ("1.19.x", "1.19.2"), ("1.18.x", "1.18.2"),
         ("1.16.x", "1.16.5"), ("1.12.x", "1.12.2")];

    private static readonly (string Label, string Index)[] Sorts =
        [("Beliebteste", "downloads"), ("Zuletzt aktualisiert", "updated"), ("Neueste", "newest"), ("Name (A–Z)", "name")];

    private const int PageSize = 20;

    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly List<PackRow> _rows = [];
    private AppServices _app = null!;
    private int _page;
    private int _request;
    private bool _ready;

    public event Action? Closed;
    public event Action<Installation>? OpenRequested;

    public ModpackPanel()
    {
        InitializeComponent();
        LoaderBox.ItemsSource = Loaders.Select(l => l.Label).ToList();
        VersionBox.ItemsSource = Versions.Select(v => v.Label).ToList();
        SortBox.ItemsSource = Sorts.Select(s => s.Label).ToList();
        LoaderBox.SelectedIndex = VersionBox.SelectedIndex = SortBox.SelectedIndex = 0;
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = LoadAsync(reset: true);
        };
    }

    public void Show(AppServices app)
    {
        _app = app;
        _ready = false;
        SearchBox.Text = "";
        (app.Settings.ModpacksAsTiles ? GridToggle : ListToggle).IsChecked = true;
        ApplyViewMode();
        _ready = true;
        _ = LoadAsync(reset: true);
    }

    private void ViewToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _app.Settings.ModpacksAsTiles = GridToggle.IsChecked == true;
        _app.SaveSettings();
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        var grid = GridToggle.IsChecked == true;
        PackList.ItemTemplate = (DataTemplate)FindResource(grid ? "PackTile" : "PackCard");
        PackList.ItemsPanel = (ItemsPanelTemplate)FindResource(grid ? "GridPanel" : "ListPanel");
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        Ui.Show(StatusText, text.Length > 0);
    }

    private async Task LoadAsync(bool reset)
    {
        var request = ++_request;
        if (reset)
        {
            _page = 0;
            _rows.RemoveAll(r => r.Idle);
            PackList.ItemsSource = null;
            Scroller.ScrollToTop();
        }
        Ui.Show(MoreButton, false);
        ShowStatus(reset ? "Suche auf Modrinth …" : "");
        try
        {
            var sort = Sorts[Math.Max(0, SortBox.SelectedIndex)].Index;
            var result = await _app.Modrinth.SearchModpacksAsync(SearchBox.Text.Trim(), _page, PageSize,
                Loaders[Math.Max(0, LoaderBox.SelectedIndex)].Value, Versions[Math.Max(0, VersionBox.SelectedIndex)].Value,
                sort == "name" ? null : sort);
            if (request != _request)
                return;
            var items = sort == "name"
                ? result.Items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList()
                : result.Items;
            foreach (var project in items.Where(p => _rows.All(r => r.Project.Id != p.Id)))
                _rows.Add(new PackRow(project));
            PackList.ItemsSource = null;
            PackList.ItemsSource = _rows;
            ShowStatus(_rows.Count == 0 ? "Keine Modpacks gefunden." : "");
            Ui.Show(MoreButton, (_page + 1) * PageSize < result.TotalHits);
        }
        catch (Exception ex)
        {
            if (request == _request)
                ShowStatus("Suche fehlgeschlagen: " + ErrorReport.Short(ex));
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
            return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && _app != null)
            _ = LoadAsync(reset: true);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        _ = LoadAsync(reset: false);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var open = new Microsoft.Win32.OpenFileDialog { Title = "Modpack öffnen", Filter = "Modrinth-Modpack (*.mrpack)|*.mrpack" };
        if (open.ShowDialog(Window.GetWindow(this)) == true && await ModpackDialogs.InstallFileAsync(_app, open.FileName))
            Closed?.Invoke();
    }

    private async void Install_Click(object sender, RoutedEventArgs e) => await InstallAsync(Ui.DataOf<PackRow>(sender), null);

    private async void Versions_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<PackRow>(sender);
        var button = (Button)sender;
        List<ContentVersion> versions;
        try
        {
            versions = (await _app.Modrinth.GetProjectVersionsAsync(row.Project.Id))
                .Where(v => v.DownloadUrl != null && v.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                .Take(12)
                .ToList();
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Versionen konnten nicht geladen werden", ex);
            return;
        }
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = 300 };
        menu.Items.Add(new MenuItem { Header = new TextBlock { Text = "VERSION WÄHLEN", Style = Ui.Resource<Style>("OverLabel") }, IsEnabled = false });
        var newest = ContentVersion.Newest(versions);
        foreach (var version in versions)
        {
            var loaders = string.Join("/", version.Loaders.Select(Loader));
            var meta = string.Join(" · ", new[] { version.GameVersions.LastOrDefault() ?? "", loaders, version.Date.ToString("dd.MM.yyyy") }
                .Where(s => s.Length > 0));
            var texts = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
            texts.Children.Add(new TextBlock { Text = version.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong") });
            texts.Children.Add(new TextBlock { Text = meta, FontSize = 11.5, Foreground = Ui.Resource<Brush>("MutedText") });
            var header = new DockPanel();
            if (version == newest)
            {
                var tag = new Border
                {
                    Height = 20,
                    Padding = new Thickness(7, 0, 7, 0),
                    CornerRadius = new CornerRadius(10),
                    Background = Ui.Frozen(Color.FromArgb(0x29, 0x3F, 0xB9, 0x50)),
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "Neueste", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Frozen(Color.FromRgb(0x6F, 0xDC, 0x80)), VerticalAlignment = VerticalAlignment.Center }
                };
                DockPanel.SetDock(tag, Dock.Right);
                header.Children.Add(tag);
            }
            header.Children.Add(texts);
            var item = new MenuItem
            {
                Header = header,
                Icon = new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(8),
                    Background = Ui.Frozen(Color.FromRgb(0x38, 0x38, 0x38)),
                    Child = new Icon { Kind = "Download", Size = 14, Foreground = Ui.Frozen(Color.FromRgb(0xBD, 0xBD, 0xBD)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            item.Click += async (_, _) => await InstallAsync(row, version);
            menu.Items.Add(item);
        }
        if (versions.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Keine installierbare Version", IsEnabled = false });
        menu.IsOpen = true;
    }

    private static string Loader(string id) => id switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => id
    };

    private async Task InstallAsync(PackRow row, ContentVersion? chosen)
    {
        if (!row.Idle)
            return;
        row.Stage = "Suche Version …";
        row.Indeterminate = true;
        row.State = "busy";
        try
        {
            var version = chosen;
            if (version == null)
            {
                var all = await _app.Modrinth.GetProjectVersionsAsync(row.Project.Id);
                version = all.Where(v => v.DownloadUrl != null && v.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                              .FirstOrDefault(v => v.IsRelease)
                          ?? all.FirstOrDefault(v => v.DownloadUrl != null && v.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidOperationException($"„{row.Project.Title}“ hat keine installierbare Version.");
            }
            row.BusyVersion = $"{version.Name} · {version.GameVersions.LastOrDefault()}";
            row.State = "busy";
            var progress = new WorkProgress(
                new Progress<string>(text => row.Stage = text.Length > 30 ? text[..29].TrimEnd('.', ' ') + " …" : text),
                new Progress<double>(fraction => row.Percent = Math.Clamp(fraction, 0, 1) * 100),
                CancellationToken.None);
            var result = await new ModpackInstaller(_app).InstallFromVersionAsync(version, row.Project.Title,
                _app.Instances.UniqueName(row.Project.Title), progress);
            row.Installed = result.Instance;
            row.DoneVersion = version.Name;
            row.State = "done";
        }
        catch (Exception ex)
        {
            row.State = "idle";
            await _app.Dialogs.ShowErrorAsync("Installation fehlgeschlagen", ex);
        }
    }

    private void OpenInstalled_Click(object sender, RoutedEventArgs e)
    {
        if (Ui.DataOf<PackRow>(sender).Installed is { } inst)
            OpenRequested?.Invoke(inst);
    }

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        var project = Ui.DataOf<PackRow>(sender).Project;
        await ShareDialogs.ShareContentAsync(_app,
            new ContentPayload { Kind = SharedContentKind.Modpack, ProjectId = project.Id, Title = project.Title });
    }

    private void Website_Click(object sender, RoutedEventArgs e) => Shell.OpenUrl(Ui.DataOf<PackRow>(sender).Project.WebsiteUrl);
}
