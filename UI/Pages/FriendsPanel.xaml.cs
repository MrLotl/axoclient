using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AxoClient.UI.Pages;

public partial class FriendsPanel : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private AppServices _app = null!;
    private HomePage _home = null!;
    private bool _loading;

    public FriendsPanel()
    {
        InitializeComponent();
    }

    public List<FriendInfo> Friends { get; private set; } = [];

    public event Action<List<FriendInfo>>? FriendsLoaded;

    public void Initialize(AppServices app, HomePage home)
    {
        _app = app;
        _home = home;
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        IsVisibleChanged += (_, _) => Refresh();
    }

    public void Refresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_app == null || _loading)
            return;

        var available = _app.Axo.Available;
        AddButton.IsEnabled = available;
        if (!available)
        {
            Friends = [];
            FriendsList.ItemsSource = null;
            ShowInfo(_app.Accounts.Session == null
                ? "Melde dich an, um deine Freunde zu sehen."
                : "Schalte unter Einstellungen „AxoClient-Symbol in der Tabliste“ ein, um Freunde hinzuzufügen.");
            return;
        }

        _loading = true;
        try
        {
            var friends = await _app.Axo.GetFriendsAsync();
            Friends = friends;
            FriendsList.ItemsSource = friends
                .OrderBy(f => f.IsIncoming ? 0 : f.IsOutgoing ? 1 : f.Playing ? 2 : f.Online ? 3 : 4)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ShowInfo(friends.Count == 0 ? "Noch keine Freunde." : null);
            FriendsLoaded?.Invoke(friends);
        }
        catch (Exception ex)
        {
            ShowInfo("Freunde konnten nicht geladen werden: " + ErrorReport.Short(ex));
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowInfo(string? text)
    {
        InfoText.Text = text;
        Ui.Show(InfoText, text != null);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = Ui.Input();
        nameBox.MaxLength = 16;
        Field.SetIcon(nameBox, "Person");
        Field.SetHint(nameBox, "z. B. Granulator444");
        nameBox.Margin = new Thickness(0);
        var form = Ui.Stack(Ui.Label("Minecraft-Name"), nameBox);
        if (!await _app.Dialogs.ShowFormAsync("Freund hinzufügen", form, "Anfrage senden",
                () => nameBox.Text.Trim().Length > 0, 420,
                "Gib den Minecraft-Namen ein. Dein Freund bekommt eine Anfrage.", "UserAdd"))
            return;
        await RunAsync(() => _app.Axo.AddFriendAsync(nameBox.Text.Trim()));
    }

    private async void Accept_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _app.Axo.AddFriendAsync(Ui.DataOf<FriendInfo>(sender).Name));

    private async void Remove_Click(object sender, RoutedEventArgs e) => await RemoveAsync(Ui.DataOf<FriendInfo>(sender));

    public async Task RemoveAsync(FriendInfo friend)
    {
        if (friend.IsFriend
            && !await _app.Dialogs.ConfirmAsync("Freund entfernen", $"{friend.Name} wirklich als Freund entfernen?",
                "Entfernen", danger: true))
            return;
        await RunAsync(() => _app.Axo.RemoveFriendAsync(friend.Uuid));
    }

    private async void Join_Click(object sender, RoutedEventArgs e) => await JoinAsync(Ui.DataOf<FriendInfo>(sender));

    public async Task JoinAsync(FriendInfo friend)
    {
        if (friend.Server != null)
            await _home.JoinServerAsync(friend.Server, friend.Version, friend.Name, ask: false);
    }

    private async void Profile_Click(object sender, RoutedEventArgs e) =>
        await FriendProfileDialog.ShowAsync(_app, Ui.DataOf<FriendInfo>(sender), this);

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Freunde", ex);
        }
        await RefreshAsync();
    }
}
