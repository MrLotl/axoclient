using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.Pages;

public class InstanceCard(Installation installation, bool running)
{
    public Installation Installation { get; } = installation;
    public bool IsRunning { get; } = running;
    public string PlayText => IsRunning ? PlayButtons.StopText : PlayButtons.PlayText;
    public string Name => Installation.Name;
    public string Description => Installation.Description;
    public LoaderType Loader => Installation.Loader;
    public string LoaderInitial => Installation.LoaderInitial;
    public BitmapSource? Image { get; } = InstanceIcons.Load(installation);
}

public partial class InstancesPage : UserControl
{
    private readonly Dictionary<RadioButton, (FrameworkElement View, Action<Installation> Show)> _tabs;
    private AppServices _app = null!;
    private Installation? _current;
    private bool _switchingTab;

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
            [OverlayTab] = (OverlayView, inst => OverlayView.Show(_app, inst)),
            [WorldsTab] = (WorldsView, inst => WorldsView.Show(_app, inst)),
            [ServersTab] = (ServersView, inst => ServersView.Show(_app, inst)),
            [PacksProfileTab] = (PackProfileView, inst => PackProfileView.Show(_app, inst)),
            [ScreenshotsTab] = (ScreenshotsView, inst => ScreenshotsView.Show(_app, inst)),
            [BackupsTab] = (BackupView, inst => BackupView.Show(_app, inst)),
            [TransferTab] = (TransferView, inst => TransferContent.Show(_app, inst)),
            [SettingsTab] = (Editor, inst => Editor.Edit(_app, inst))
        };

        WorldsView.PlayWorldRequested += (inst, world) => PlayRequested?.Invoke(inst, new QuickPlay(World: world));
        ServersView.JoinRequested += (inst, server) => PlayRequested?.Invoke(inst, new QuickPlay(Server: server));
        ModpackView.Closed += ShowList;
        UpdatesView.Closed += ShowList;
        Editor.Saved += Editor_Saved;
        Editor.Deleted += ShowList;
        Editor.Cancelled += () =>
        {
            if (_current == null)
                ShowList();
            else
                Editor.Edit(_app, _current);
        };
    }

    public void Initialize(AppServices app)
    {
        _app = app;
        _app.Instances.Changed += RefreshList;
        _app.Games.Changed += RefreshList;
        (app.Settings.InstancesAsTiles ? TileViewToggle : ListViewToggle).IsChecked = true;
        ApplyViewMode();
        RefreshList();
    }

    private void RefreshList()
    {
        InstanceList.ItemsSource = _app.Instances.All.Select(i => new InstanceCard(i, _app.Games.IsRunning(i))).ToList();
        if (_current == null)
            return;
        ShowHeader(_current);
        if (OverviewView.IsVisible)
            OverviewView.Refresh();
    }

    private void ShowHeader(Installation inst)
    {
        DetailTitle.Text = inst.Name;
        DetailSubtitle.Text = inst.Description;
        PlayButtons.Apply(DetailPlayButton, _app.Games.IsRunning(inst));
    }

    private void ViewToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_app == null)
            return;
        _app.Settings.InstancesAsTiles = TileViewToggle.IsChecked == true;
        _app.SaveSettings();
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        var tiles = TileViewToggle.IsChecked == true;
        InstanceList.ItemTemplate = (DataTemplate)FindResource(tiles ? "TileTemplate" : "ListTemplate");
        InstanceList.ItemsPanel = (ItemsPanelTemplate)FindResource(tiles ? "TilePanel" : "ListPanel");
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is InstanceCard card)
            ShowDetail(card.Installation);
    }

    public void ShowList()
    {
        _current = null;
        ShowOnly(OverviewPanel);
    }

    private void ShowOnly(UIElement panel)
    {
        foreach (var candidate in new UIElement[] { OverviewPanel, DetailPanel, ModpackView, UpdatesView })
            Ui.Show(candidate, candidate == panel);
    }

    private void Modpacks_Click(object sender, RoutedEventArgs e)
    {
        ShowOnly(ModpackView);
        ModpackView.Show(_app);
    }

    private void Updates_Click(object sender, RoutedEventArgs e)
    {
        ShowOnly(UpdatesView);
        UpdatesView.Show(_app);
    }

    private async void ForeignImport_Click(object sender, RoutedEventArgs e) => await ForeignImportDialog.RunAsync(_app);

    private async void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        if (_current != null && await UpgradeDialog.RunAsync(_app, _current) is { } upgraded)
            ShowDetail(upgraded);
    }

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        if (_current != null)
            await ShareDialogs.ShareInstanceAsync(_app, _current);
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        _current = null;
        DetailTitle.Text = "Neue Instanz";
        DetailSubtitle.Text = "Name, Mod-Loader und Minecraft-Version wählen";
        DetailTabs.Visibility = Visibility.Collapsed;
        DetailActions.Visibility = Visibility.Collapsed;
        ShowTabView(Editor);
        Editor.Edit(_app, null);
        ShowOnly(DetailPanel);
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var inst = Ui.DataOf<InstanceCard>(sender).Installation;
        await PlayButtons.PlayOrStopAsync(_app, inst, () => PlayRequested?.Invoke(inst, null));
    }

    private async void DetailPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_current is { } inst)
            await PlayButtons.PlayOrStopAsync(_app, inst, () => PlayRequested?.Invoke(inst, null));
    }

    private void ShowDetail(Installation inst)
    {
        _current = inst;
        ShowHeader(inst);
        DetailTabs.Visibility = Visibility.Visible;
        DetailActions.Visibility = Visibility.Visible;
        ShowOnly(DetailPanel);

        _switchingTab = true;
        foreach (var tab in _tabs.Keys)
            tab.IsChecked = tab == OverviewTab;
        _switchingTab = false;
        ShowTab();
    }

    private void DetailTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_switchingTab && _current != null)
            ShowTab();
    }

    private void ShowTab()
    {
        if (_current == null || _tabs.FirstOrDefault(t => t.Key.IsChecked == true).Value is not { View: not null } tab)
            return;
        ShowTabView(tab.View);
        tab.Show(_current);
    }

    private void ShowTabView(FrameworkElement view)
    {
        foreach (var candidate in _tabs.Values.Select(t => t.View).Distinct())
            Ui.Show(candidate, candidate == view);
    }

    private void Editor_Saved(Installation inst, bool isNew)
    {
        if (isNew)
            ShowDetail(inst);
        else
            ShowHeader(inst);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowList();

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (_current != null)
            Shell.OpenFolder(_current.GameDir, create: true);
    }
}
