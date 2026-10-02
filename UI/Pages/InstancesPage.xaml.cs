using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.Pages;

public partial class InstancesPage : UserControl
{
    private readonly Dictionary<RadioButton, (FrameworkElement View, Action<Installation> Show)> _tabs;
    private AppServices _app = null!;
    private Installation? _current;
    private bool _switchingTab;
    private bool _ready;

    public event Action<Installation, QuickPlay?>? PlayRequested;

    public InstancesPage()
    {
        InitializeComponent();
        _tabs = new Dictionary<RadioButton, (FrameworkElement, Action<Installation>)>
        {
            [OverviewTab] = (OverviewView, inst => OverviewView.Show(_app, inst)),
            [ModsTab] = (ContentView, inst => ContentView.Show(_app, inst, ContentType.Mod)),
            [PacksTab] = (ContentView, inst => ContentView.Show(_app, inst, ContentType.ResourcePack)),
            [ShadersTab] = (ContentView, inst => ContentView.Show(_app, inst, ContentType.Shader)),
            [WorldsTab] = (WorldsView, inst => WorldsView.Show(_app, inst)),
            [ServersTab] = (ServersView, inst => ServersView.Show(_app, inst)),
            [ScreenshotsTab] = (ScreenshotsView, inst => ScreenshotsView.Show(_app, inst)),
            [SettingsTab] = (Editor, inst => Editor.Edit(_app, inst))
        };

        OverviewView.OpenRequested += section => TabFor(section).IsChecked = true;
        WorldsView.PlayWorldRequested += (inst, world) => PlayRequested?.Invoke(inst, new QuickPlay(World: world));
        ServersView.JoinRequested += (inst, server) => PlayRequested?.Invoke(inst, new QuickPlay(Server: server));
        ModpackView.Closed += ShowList;
        ModpackView.OpenRequested += Open;
        Editor.Saved += (inst, _) =>
        {
            ShowHeader(inst);
            RefreshList();
        };
        Editor.Deleted += ShowList;
        Editor.Upgraded += Open;
        Creator.Saved += (inst, _) => Open(inst, null);
        Creator.Cancelled += ShowList;
        Creator.ModpacksRequested += () => Modpacks_Click(this, new RoutedEventArgs());
    }

    private void Open(Installation inst) => Open(inst, null);

    public void Initialize(AppServices app)
    {
        _app = app;
        _app.Instances.Changed += () => Dispatcher.InvokeAsync(RefreshList);
        _app.Games.Changed += () => Dispatcher.InvokeAsync(RefreshList);
        (app.Settings.InstancesAsTiles ? GridToggle : ListToggle).IsChecked = true;
        _ready = true;
        ApplyViewMode();
        RefreshList();
    }

    private void RefreshList()
    {
        var items = _app.Instances.All.Select(i => new InstanceItem(i, _app.Games.IsRunning(i))).ToList();
        InstanceList.ItemsSource = items;
        CountText.Text = items.Count.ToString();
        Ui.Show(EmptyState, items.Count == 0);
        if (_current == null)
            return;
        if (!_app.Instances.All.Contains(_current))
        {
            ShowList();
            return;
        }
        ShowHeader(_current);
    }

    private void ViewToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _app.Settings.InstancesAsTiles = GridToggle.IsChecked == true;
        _app.SaveSettings();
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        var grid = GridToggle.IsChecked == true;
        InstanceList.ItemTemplate = (DataTemplate)FindResource(grid ? "CardTemplate" : "ListTemplate");
        InstanceList.ItemsPanel = (ItemsPanelTemplate)FindResource(grid ? "GridPanel" : "ListPanel");
    }

    private void ShowOnly(UIElement panel)
    {
        foreach (var candidate in new UIElement[] { ListView, DetailView, Creator, ModpackView })
            Ui.Show(candidate, candidate == panel);
    }

    public void ShowList()
    {
        _current = null;
        ShowOnly(ListView);
        RefreshList();
    }

    private void Open_Click(object sender, RoutedEventArgs e) => Open(Ui.DataOf<InstanceItem>(sender).Installation, null);

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var inst = Ui.DataOf<InstanceItem>(sender).Installation;
        await PlayButtons.PlayOrStopAsync(_app, inst, () => PlayRequested?.Invoke(inst, null));
    }

    private void Modpacks_Click(object sender, RoutedEventArgs e)
    {
        ShowOnly(ModpackView);
        ModpackView.Show(_app);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ImportButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(MenuItem("Aus anderem Launcher", "Modrinth App, CurseForge, Prism, Lunar, Feather …", "Import",
            async () => await ForeignImportDialog.RunAsync(_app)));
        menu.Items.Add(MenuItem("Aus Datei", ".mrpack, .zip oder geteiltes AxoClient-Paket", "Folder", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Modpacks und Pakete (*.mrpack;*.zip;*.json)|*.mrpack;*.zip;*.json",
                Title = "Instanz importieren"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                await DropHandler.HandleAsync(_app, [dialog.FileName]);
        }));
        menu.IsOpen = true;
    }

    public static MenuItem MenuItem(string title, string detail, string icon, Func<Task> click)
    {
        var texts = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        texts.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        texts.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Foreground = Ui.Resource<Brush>("MutedText") });
        var item = new MenuItem
        {
            Header = texts,
            Icon = new Icon { Kind = icon, Size = 16, Foreground = Ui.Resource<Brush>("AccentText") }
        };
        item.Click += async (_, _) => await click();
        return item;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        _current = null;
        ShowOnly(Creator);
        Creator.Edit(_app, null);
    }

    public void Open(Installation inst, InstanceSection? section)
    {
        _current = inst;
        ShowHeader(inst);
        ShowOnly(DetailView);

        _switchingTab = true;
        var target = section is { } s ? TabFor(s) : OverviewTab;
        foreach (var tab in _tabs.Keys)
            tab.IsChecked = tab == target;
        _switchingTab = false;
        ShowTab();
    }

    public async Task OpenUpdatesAsync(Installation inst)
    {
        Open(inst, InstanceSection.Mods);
        await ContentView.OpenUpdatesAsync();
    }

    private void ShowHeader(Installation inst)
    {
        DetailTitle.Text = inst.Name;
        var banner = InstanceIcons.LoadBanner(inst);
        HeroArt.Background = banner != null
            ? new ImageBrush(banner) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center }
            : InstanceText.Placeholder(inst);

        var mods = inst.CanUseMods ? FileOps.CountEntries(inst.ModsDir, "*.jar") : 0;
        HeroChips.Children.Clear();
        HeroChips.Children.Add(HeroChip(InstanceText.Version(inst)));
        if (inst.CanUseMods)
            HeroChips.Children.Add(HeroChip(Formats.Count(mods, "Mod", "Mods")));

        Field.SetCount(ModsTab, inst.CanUseMods ? mods.ToString() : "");
        Field.SetCount(PacksTab, Count(inst.ContentDir(ContentType.ResourcePack), "*.zip", true));
        Field.SetCount(ShadersTab, Count(inst.ContentDir(ContentType.Shader), "*.zip", true));
        Field.SetCount(WorldsTab, WorldStore.Count(inst.SavesDir).ToString());
        Field.SetCount(ServersTab, new ServerStore(inst.GameDir).Addresses().Count.ToString());
        Field.SetCount(ScreenshotsTab, FileOps.CountEntries(inst.ScreenshotsDir, "*.png").ToString());

        var running = _app.Games.IsRunning(inst);
        HeroPlayIcon.Kind = running ? "Stop" : "Play";
        HeroPlayText.Text = running ? "Beenden" : "Spielen";
    }

    private static string Count(string dir, string pattern, bool withFolders)
    {
        var count = FileOps.CountEntries(dir, pattern);
        if (withFolders && Directory.Exists(dir))
        {
            try
            {
                count += Directory.GetDirectories(dir).Length;
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Ordner zählen", ex);
            }
        }
        return count.ToString();
    }

    private static Border HeroChip(string text) => new()
    {
        Height = 24,
        Padding = new Thickness(9, 0, 9, 0),
        Margin = new Thickness(0, 0, 8, 0),
        CornerRadius = new CornerRadius(6),
        Background = Ui.Frozen(Color.FromArgb(0x73, 0, 0, 0)),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ui.Resource<Brush>("TextStrong"),
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        if (_current != null)
            await ShareDialogs.ShareInstanceAsync(_app, _current);
    }

    private async void DetailPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_current is { } inst)
            await PlayButtons.PlayOrStopAsync(_app, inst, () => PlayRequested?.Invoke(inst, null));
    }

    private RadioButton TabFor(InstanceSection section) => section switch
    {
        InstanceSection.Mods => ModsTab,
        InstanceSection.ResourcePacks => PacksTab,
        InstanceSection.Shaders => ShadersTab,
        InstanceSection.Worlds => WorldsTab,
        InstanceSection.Servers => ServersTab,
        InstanceSection.Screenshots => ScreenshotsTab,
        _ => SettingsTab
    };

    private void DetailTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_switchingTab && _current != null)
            ShowTab();
    }

    private void ShowTab()
    {
        if (_current == null || _tabs.FirstOrDefault(t => t.Key.IsChecked == true).Value is not { View: not null } tab)
            return;
        foreach (var candidate in _tabs.Values.Select(t => t.View).Distinct())
            Ui.Show(candidate, candidate == tab.View);
        tab.Show(_current);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowList();

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (_current != null)
            Shell.OpenFolder(_current.GameDir, create: true);
    }
}
