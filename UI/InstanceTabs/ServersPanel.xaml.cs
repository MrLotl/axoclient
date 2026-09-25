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

    public event Action<Installation, string>? JoinRequested;

    public ServersPanel()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation inst)
    {
        _app = app;
        _inst = inst;
        _store = new ServerStore(inst.GameDir);
        StatusText.Text = "";
        Reload();
    }

    private void Reload()
    {
        _servers = _store.Load();
        ServerList.ItemsSource = null;
        ServerList.ItemsSource = _servers;
        Header.Text = $"Server ({_servers.Count})";
        Ui.Show(EmptyText, _servers.Count == 0);
        if (_store.LoadError is { } error)
            StatusText.Text = "Die gespeicherte Serverliste konnte nicht gelesen werden: " + ErrorReport.Short(error);

        var request = ++_request;
        _ = Task.WhenAll(_servers.Select(s => s.RefreshStatusAsync(() => request == _request, withMotd: true)));
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Join_Click(object sender, RoutedEventArgs e) => JoinRequested?.Invoke(_inst, Ui.DataOf<ServerEntry>(sender).Address);

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        var server = Ui.DataOf<ServerEntry>(sender);
        await ShareDialogs.ShareServerAsync(_app, server.Name, server.Address);
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await EditServerAsync(null);

    private async void Edit_Click(object sender, RoutedEventArgs e) => await EditServerAsync(Ui.DataOf<ServerEntry>(sender));

    private async Task EditServerAsync(ServerEntry? server)
    {
        var nameBox = Ui.Input(server?.Name ?? "");
        var addressBox = Ui.Input(server?.Address ?? "");
        addressBox.Margin = new Thickness(0);
        var problem = Ui.Problem();
        var form = Ui.Stack(Ui.Label("Name"), nameBox,
            Ui.Label("Adresse (z.B. play.example.net oder 1.2.3.4:25565)"), addressBox, problem);

        var confirmed = await _app.Dialogs.ShowFormAsync(
            server == null ? "Server hinzufügen" : $"\"{server.Name}\" bearbeiten", form,
            server == null ? "Hinzufügen" : "Speichern",
            () =>
            {
                var valid = Ui.ShowProblem(problem, addressBox.Text.Trim().Length > 0 ? null : "Bitte eine Adresse eingeben.");
                if (!valid)
                    addressBox.Focus();
                return valid;
            });
        if (!confirmed)
            return;

        var name = string.IsNullOrWhiteSpace(nameBox.Text) ? "Minecraft-Server" : nameBox.Text.Trim();
        var address = addressBox.Text.Trim();
        if (server != null)
        {
            server.Name = name;
            server.Address = address;
        }
        else
        {
            _servers.Add(ServerStore.Create(name, address));
        }
        await SaveAsync("Serverliste gespeichert.");
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var server = Ui.DataOf<ServerEntry>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Server entfernen",
                $"\"{server.Name}\" ({server.Address}) aus der Serverliste entfernen?", "Entfernen", danger: true))
            return;
        _servers.Remove(server);
        await SaveAsync("");
    }

    private async Task SaveAsync(string success)
    {
        try
        {
            _store.Save(_servers);
            StatusText.Text = success;
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Speichern fehlgeschlagen", ex);
        }
        Reload();
    }
}
