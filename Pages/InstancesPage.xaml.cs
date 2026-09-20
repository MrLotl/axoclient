using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace McLauncher.Pages;

/// <summary>Anzeige einer Instanz in Liste/Kacheln; Bild = eigenes Bild oder Vorschaubild der zuletzt gespielten Welt.</summary>
public class InstanceCard(Installation installation, bool running)
{
    public bool IsRunning { get; } = running;
    public string PlayText => IsRunning ? McLauncher.Pages.PlayButtons.StopText : McLauncher.Pages.PlayButtons.PlayText;
    public Installation Installation { get; } = installation;
    public string Name => Installation.Name;
    public string Description => Installation.Description;
    public LoaderType Loader => Installation.Loader;
    public string LoaderInitial => Installation.LoaderInitial;
    public BitmapSource? Image { get; } = InstanceIcons.Load(installation);
}

/// <summary>Übersicht aller Instanzen und Detailansicht mit Mods, Ressourcenpaketen, Shadern und Einstellungen.</summary>
public partial class InstancesPage : UserControl
{
    private AppState _app = null!;
    private Installation? _current; // null = neue Instanz wird angelegt
    private bool _switchingTab;

    /// <summary>Die Instanz soll gestartet werden, optional direkt in eine Welt oder auf einen Server.</summary>
    public event Action<Installation, QuickPlay?>? PlayRequested;

    public InstancesPage()
    {
        InitializeComponent();
        WorldsView.PlayWorldRequested += (inst, world) => PlayRequested?.Invoke(inst, new QuickPlay(World: world));
        ServersView.JoinRequested += (inst, server) => PlayRequested?.Invoke(inst, new QuickPlay(Server: server));
        ModpackView.Closed += ShowList;
        Editor.Saved += Editor_Saved;
        Editor.Deleted += _ => ShowList();
        Editor.Cancelled += () =>
        {
            if (_current == null)
                ShowList();
            else
                Editor.Edit(_app, _current); // Änderungen verwerfen
        };
    }

    public void Initialize(AppState app)
    {
        _app = app;
        _app.InstallationsChanged += RefreshList;
        _app.RunningGamesChanged += RefreshList;
        (app.Settings.InstancesAsTiles ? TileViewToggle : ListViewToggle).IsChecked = true;
        ApplyViewMode();
        RefreshList();
    }

    private void RefreshList()
    {
        InstanceList.ItemsSource = _app.Settings.Installations.Select(i => new InstanceCard(i, _app.IsRunning(i))).ToList();
        if (_current != null)
        {
            DetailTitle.Text = _current.Name;
            DetailSubtitle.Text = _current.Description;
            PlayButtons.Apply(DetailPlayButton, _app.IsRunning(_current));
        }
    }

    // ---------- Liste / Kacheln ----------

    private void ViewToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_app == null)
            return; // beim Aufbau, Initialize setzt die Ansicht
        _app.Settings.InstancesAsTiles = TileViewToggle.IsChecked == true;
        _app.Save();
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        var tiles = TileViewToggle.IsChecked == true;
        InstanceList.ItemTemplate = (DataTemplate)FindResource(tiles ? "TileTemplate" : "ListTemplate");
        InstanceList.ItemsPanel = (ItemsPanelTemplate)FindResource(tiles ? "TilePanel" : "ListPanel");
    }

    /// <summary>Ein Klick auf die Karte (nicht auf "Spielen") öffnet die Instanz zum Bearbeiten.</summary>
    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is InstanceCard card)
            ShowDetail(card.Installation);
    }

    // ---------- Übersicht ----------

    public void ShowList()
    {
        _current = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        ModpackView.Visibility = Visibility.Collapsed;
        OverviewPanel.Visibility = Visibility.Visible;
    }

    private void Modpacks_Click(object sender, RoutedEventArgs e)
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Collapsed;
        ModpackView.Visibility = Visibility.Visible;
        ModpackView.Show(_app);
    }

    /// <summary>Öffnet eine Datei: Instanz oder Overlay von einem Freund (.json) bzw. ein Modpack (.mrpack).</summary>
    private async void Import_Click(object sender, RoutedEventArgs e) => await ShareUi.ImportFileAsync(_app);

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        if (_current != null)
            await ShareUi.ShareInstanceAsync(_app, _current);
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        _current = null;
        DetailTitle.Text = "Neue Instanz";
        DetailSubtitle.Text = "Name, Mod-Loader und Minecraft-Version wählen";
        DetailTabs.Visibility = Visibility.Collapsed;
        DetailActions.Visibility = Visibility.Collapsed;
        ContentView.Visibility = WorldsView.Visibility = ServersView.Visibility = TransferView.Visibility =
            Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        Editor.Edit(_app, null);
        OverviewPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // Klick nicht zusätzlich als Karten-Klick werten
        var inst = ((InstanceCard)((FrameworkElement)sender).DataContext).Installation;
        if (_app.IsRunning(inst))
            _ = _app.StopGameAsync(inst);
        else
            PlayRequested?.Invoke(inst, null);
    }

    // ---------- Detailansicht ----------

    private void ShowDetail(Installation inst, bool openSettings = false)
    {
        _current = inst;
        PlayButtons.Apply(DetailPlayButton, _app.IsRunning(inst));
        DetailTitle.Text = inst.Name;
        DetailSubtitle.Text = inst.Description;
        DetailTabs.Visibility = Visibility.Visible;
        DetailActions.Visibility = Visibility.Visible;
        OverviewPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;

        // Vanilla hat keine Mods, daher mit Ressourcenpaketen beginnen
        var tab = openSettings ? SettingsTab : inst.Loader == LoaderType.Vanilla ? PacksTab : ModsTab;
        _switchingTab = true;
        foreach (var t in new[] { ModsTab, PacksTab, ShadersTab, WorldsTab, ServersTab, TransferTab, SettingsTab })
            t.IsChecked = t == tab;
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
        if (_current == null)
            return;

        var content = ModsTab.IsChecked == true || PacksTab.IsChecked == true || ShadersTab.IsChecked == true;
        ContentView.Visibility = content ? Visibility.Visible : Visibility.Collapsed;
        WorldsView.Visibility = WorldsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ServersView.Visibility = ServersTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TransferView.Visibility = TransferTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = SettingsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (content)
        {
            var type = ModsTab.IsChecked == true ? ContentType.Mod
                : ShadersTab.IsChecked == true ? ContentType.Shader
                : ContentType.ResourcePack;
            ContentView.Show(_app, _current, type);
        }
        else if (WorldsTab.IsChecked == true)
            WorldsView.Show(_app, _current);
        else if (ServersTab.IsChecked == true)
            ServersView.Show(_app, _current);
        else if (TransferTab.IsChecked == true)
            TransferContent.Show(_app, _current);
        else
            Editor.Edit(_app, _current);
    }

    private void Editor_Saved(Installation inst, bool isNew)
    {
        if (isNew)
            ShowDetail(inst); // direkt weiter zu den Mods der neuen Instanz
        else
        {
            DetailTitle.Text = inst.Name;
            DetailSubtitle.Text = inst.Description;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowList();

    private void DetailPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
            return;
        if (_app.IsRunning(_current))
            _ = _app.StopGameAsync(_current);
        else
            PlayRequested?.Invoke(_current, null);
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
            return;
        Directory.CreateDirectory(_current.GameDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_current.GameDir}\"") { UseShellExecute = true });
    }
}
