using System.Windows;

namespace McLauncher.Pages;

/// <summary>
/// Startseite, rechte Spalte unten: die Serverliste (servers.dat) der gewählten Instanz mit Live-Status.
/// Ein Klick auf "Beitreten" startet genau diese Instanz und verbindet direkt.
/// </summary>
public partial class HomePage
{
    private List<ServerEntry> _servers = [];
    private int _serverPingRequest;

    /// <summary>Liest die Serverliste der gewählten Instanz neu und fragt die Status ab.</summary>
    private void RefreshServers()
    {
        if (_app == null)
            return;

        var inst = _app.SelectedInstallation;
        _servers = inst == null ? [] : new ServerStore(inst.GameDir).Load();
        ServerList.ItemsSource = null;
        ServerList.ItemsSource = _servers;
        ServersHeader.Text = _servers.Count == 0 ? "Server" : $"Server ({_servers.Count})";
        RefreshServersButton.IsEnabled = inst != null;

        var info = inst == null
            ? "Lege zuerst eine Instanz an."
            : _servers.Count == 0
                ? $"\"{inst.Name}\" hat noch keine Server. Du kannst sie im Spiel oder unter Instanzen hinzufügen."
                : null;
        ServersInfo.Text = info;
        ServersInfo.Visibility = info == null ? Visibility.Collapsed : Visibility.Visible;

        _ = PingServersAsync();
    }

    private async Task PingServersAsync()
    {
        var request = ++_serverPingRequest;
        await Task.WhenAll(_servers.Select(async server =>
        {
            server.Status = "Status wird abgefragt...";
            server.Online = false;
            try
            {
                var result = await ServerPing.PingAsync(server.Address);
                if (request != _serverPingRequest)
                    return;
                server.Online = true;
                server.Status = $"{result.Online}/{result.Max} Spieler · {result.LatencyMs} ms";
                if (result.Favicon != null)
                    server.Icon = ServerEntry.DecodeIcon(result.Favicon) ?? server.Icon;
            }
            catch
            {
                if (request == _serverPingRequest)
                    server.Status = "Nicht erreichbar";
            }
        }));
    }

    private void RefreshServers_Click(object sender, RoutedEventArgs e) => RefreshServers();

    private async void JoinServer_Click(object sender, RoutedEventArgs e)
    {
        var server = (ServerEntry)((FrameworkElement)sender).DataContext;
        // Ohne Versionsangabe nimmt JoinServerAsync die gerade gewählte Instanz
        await JoinServerAsync(server.Address, version: null, friendName: null, ask: false);
    }
}
