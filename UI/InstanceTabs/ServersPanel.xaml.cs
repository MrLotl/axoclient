using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.InstanceTabs;

public partial class ServersPanel : UserControl
{
    private AppServices _app = null!;
    private Installation _inst = null!;
    private ServerStore _store = null!;
    private List<ServerEntry> _servers = [];
    private int _request;
    private bool _ready;

    public event Action<Installation, string>? JoinRequested;

    public ServersPanel()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation inst)
    {
        _ready = false;
        _app = app;
        _inst = inst;
        _store = new ServerStore(inst.GameDir);
        FilterBox.Text = "";
        ShowStatus("");
        (app.Settings.ContentAsTiles ? GridToggle : ListToggle).IsChecked = true;
        ApplyViewMode();
        _ready = true;
        Reload();
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        Ui.Show(StatusText, text.Length > 0);
    }

    private void Reload()
    {
        _servers = _store.Load();
        if (_store.LoadError is { } error)
            ShowStatus("Die gespeicherte Serverliste konnte nicht gelesen werden: " + ErrorReport.Short(error));
        ApplyFilter();
        var request = ++_request;
        _ = Task.WhenAll(_servers.Select(s => s.RefreshStatusAsync(() => request == _request, withMotd: true)));
    }

    private void ApplyFilter()
    {
        var query = FilterBox.Text.Trim();
        var shown = _servers.Where(s => query.Length == 0
                                        || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                        || s.Address.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        ServerList.ItemsSource = shown;
        SummaryText.Text = Formats.Count(_servers.Count, "Server", "Server");
        EmptyText.Text = _servers.Count == 0 ? "Noch keine Server. Füge einen hinzu oder tritt im Spiel einem bei." : "Keine Server gefunden.";
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
        ServerList.ItemTemplate = (DataTemplate)FindResource(grid ? "CardTemplate" : "ListTemplate");
        ServerList.ItemsPanel = (ItemsPanelTemplate)FindResource(grid ? "GridPanel" : "ListPanel");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Join_Click(object sender, RoutedEventArgs e) => JoinRequested?.Invoke(_inst, Ui.DataOf<ServerEntry>(sender).Address);

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        var server = Ui.DataOf<ServerEntry>(sender);
        await ShareDialogs.ShareServerAsync(_app, server.Name, server.Address);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (await ServerForm.AskAsync(_app, "Server hinzufügen",
                $"Für „{_inst.Name}“. Der Server erscheint auch im Spiel in der Serverliste.", "Hinzufügen") is not { } server)
            return;
        _servers.Add(ServerStore.Create(server.Name, server.Address));
        await SaveAsync();
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        var entry = Ui.DataOf<ServerEntry>(sender);
        if (await ServerForm.AskAsync(_app, "Server bearbeiten", $"„{entry.Name}“ in „{_inst.Name}“", "Speichern",
                entry.Name, entry.Address) is not { } server)
            return;
        entry.Name = server.Name;
        entry.Address = server.Address;
        await SaveAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var server = Ui.DataOf<ServerEntry>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Server entfernen",
                $"„{server.Name}“ ({server.Address}) aus der Serverliste entfernen?", "Entfernen", danger: true))
            return;
        _servers.Remove(server);
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            _store.Save(_servers);
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Speichern fehlgeschlagen", ex);
        }
        Reload();
    }
}
