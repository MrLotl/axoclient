using System.Windows;
using System.Windows.Controls;

namespace McLauncher.Pages;

/// <summary>Serverliste einer Instanz (servers.dat): Status, hinzufügen, bearbeiten, direkt beitreten.</summary>
public partial class ServersPanel : UserControl
{
    private AppState _app = null!;
    private Installation _inst = null!;
    private ServerStore _store = null!;
    private List<ServerEntry> _servers = [];
    private int _loadRequest;

    /// <summary>Instanz soll starten und direkt mit dem Server verbinden (Adresse).</summary>
    public event Action<Installation, string>? JoinRequested;

    public ServersPanel()
    {
        InitializeComponent();
    }

    public void Show(AppState app, Installation inst)
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
        EmptyText.Visibility = _servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = PingAllAsync();
    }

    private async Task PingAllAsync()
    {
        var request = ++_loadRequest;
        await Task.WhenAll(_servers.Select(async server =>
        {
            server.Status = "Status wird abgefragt...";
            server.Online = false;
            try
            {
                var result = await ServerPing.PingAsync(server.Address);
                if (request != _loadRequest)
                    return;
                server.Online = true;
                server.Status = $"{result.Online}/{result.Max} Spieler · {result.LatencyMs} ms" +
                                (string.IsNullOrEmpty(result.Motd) ? "" : $" · {result.Motd}");
                if (result.Favicon != null)
                    server.Icon = ServerEntry.DecodeIcon(result.Favicon) ?? server.Icon;
            }
            catch
            {
                if (request == _loadRequest)
                    server.Status = "Nicht erreichbar";
            }
        }));
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private static ServerEntry ServerOf(object sender) => (ServerEntry)((FrameworkElement)sender).DataContext;

    private void Join_Click(object sender, RoutedEventArgs e) => JoinRequested?.Invoke(_inst, ServerOf(sender).Address);

    // ---------- Hinzufügen / Bearbeiten (Popup) ----------

    private async void Add_Click(object sender, RoutedEventArgs e) =>
        await EditServerAsync(null);

    private async void Edit_Click(object sender, RoutedEventArgs e) =>
        await EditServerAsync(ServerOf(sender));

    /// <summary>Zeigt das Popup mit Name und Adresse; <paramref name="server"/> = null legt einen neuen Server an.</summary>
    private async Task EditServerAsync(ServerEntry? server)
    {
        var nameBox = new TextBox { Style = (Style)FindResource("LauncherTextBox"), Text = server?.Name ?? "" };
        var addressBox = new TextBox { Style = (Style)FindResource("LauncherTextBox"), Text = server?.Address ?? "" };
        var hint = new TextBlock
        {
            Text = "Bitte eine Adresse eingeben.",
            Foreground = (System.Windows.Media.Brush)FindResource("Danger"),
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };
        var form = new StackPanel { Width = 392 };
        form.Children.Add(new TextBlock { Text = "Name", Style = (Style)FindResource("FieldLabel") });
        form.Children.Add(nameBox);
        form.Children.Add(new TextBlock
        {
            Text = "Adresse (z.B. play.example.net oder 1.2.3.4:25565)",
            Style = (Style)FindResource("FieldLabel"),
            Margin = new Thickness(0, 14, 0, 6)
        });
        form.Children.Add(addressBox);
        form.Children.Add(hint);

        var confirmed = await _app.Dialogs.ShowFormAsync(
            server == null ? "Server hinzufügen" : $"\"{server.Name}\" bearbeiten", form,
            server == null ? "Hinzufügen" : "Speichern",
            validate: () =>
            {
                var valid = addressBox.Text.Trim().Length > 0;
                hint.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
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

        try
        {
            _store.Save(_servers);
            StatusText.Text = "Serverliste gespeichert.";
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowMessageAsync("Speichern fehlgeschlagen", ex.Message);
        }
        Reload();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var server = ServerOf(sender);
        if (!await _app.Dialogs.ConfirmAsync("Server entfernen",
                $"\"{server.Name}\" ({server.Address}) aus der Serverliste entfernen?", "Entfernen", danger: true))
            return;
        _servers.Remove(server);
        _store.Save(_servers);
        Reload();
    }
}
