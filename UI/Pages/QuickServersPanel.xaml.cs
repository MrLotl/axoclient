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

    public int Count => _servers.Count;

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
        AddButton.IsEnabled = inst != null;

        var info = inst == null
            ? "Lege zuerst eine Instanz an."
            : _servers.Count == 0
                ? $"„{inst.Name}“ hat noch keine Server."
                : null;
        InfoText.Text = info;
        Ui.Show(InfoText, info != null);

        var request = ++_request;
        _ = Task.WhenAll(_servers.Select(s => s.RefreshStatusAsync(() => request == _request, withMotd: false)));
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Instances.Selected is not { } inst)
            return;
        if (await ServerForm.AskAsync(_app, "Server hinzufügen",
                "Der Server erscheint danach in deiner Liste und kann direkt beigetreten werden.", "Hinzufügen") is not
            { } server)
            return;
        await UiRun.GuardAsync(_app, "Server konnte nicht gespeichert werden", () =>
        {
            var store = new ServerStore(inst.GameDir);
            var all = store.Load();
            all.Add(ServerStore.Create(server.Name, server.Address));
            store.Save(all);
            return Task.CompletedTask;
        });
        Refresh();
    }

    private async void Join_Click(object sender, RoutedEventArgs e) =>
        await _home.JoinServerAsync(Ui.DataOf<ServerEntry>(sender).Address, version: null, friendName: null, ask: false);
}
