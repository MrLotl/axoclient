using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.Pages;

public partial class QuickServersPanel : UserControl
{
    private AppServices _app = null!;
    private HomePage _home = null!;
    private List<ServerEntry> _servers = [];
    private int _request;

    public QuickServersPanel()
    {
        InitializeComponent();
    }

    public void Initialize(AppServices app, HomePage home)
    {
        _app = app;
        _home = home;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                Refresh();
        };
    }

    public void Refresh()
    {
        if (_app == null)
            return;

        var inst = _app.Instances.Selected;
        _servers = inst == null ? [] : new ServerStore(inst.GameDir).Load();
        ServerList.ItemsSource = _servers;
        Header.Text = _servers.Count == 0 ? "Server" : $"Server ({_servers.Count})";
        RefreshButton.IsEnabled = inst != null;

        var info = inst == null
            ? "Lege zuerst eine Instanz an."
            : _servers.Count == 0
                ? $"\"{inst.Name}\" hat noch keine Server. Du kannst sie im Spiel oder unter Instanzen hinzufügen."
                : null;
        InfoText.Text = info;
        Ui.Show(InfoText, info != null);

        var request = ++_request;
        _ = Task.WhenAll(_servers.Select(s => s.RefreshStatusAsync(() => request == _request, withMotd: false)));
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private async void Join_Click(object sender, RoutedEventArgs e) =>
        await _home.JoinServerAsync(Ui.DataOf<ServerEntry>(sender).Address, version: null, friendName: null, ask: false);
}
