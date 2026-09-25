using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AxoClient.UI.Pages;

public partial class FriendsPanel : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private AppServices _app = null!;
    private HomePage _home = null!;
    private bool _loadingFriends;
    private bool _loadingShares;

    public FriendsPanel()
    {
        InitializeComponent();
    }

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
        if (_app == null || !IsVisible || _loadingFriends)
            return;

        var available = _app.Axo.Available;
        AddButton.IsEnabled = RefreshButton.IsEnabled = available;
        if (!available)
        {
            FriendsList.ItemsSource = null;
            Header.Text = "Freunde";
            SharesPanel.Visibility = Visibility.Collapsed;
            ShowInfo(_app.Accounts.Session == null
                ? "Melde dich an, um deine Freunde zu sehen."
                : "Schalte unter Einstellungen \"Axolotl-Symbol in der Tabliste\" ein, um Freunde hinzuzufügen " +
                  "und ihnen auf Server zu folgen.");
            return;
        }

        _loadingFriends = true;
        _ = RefreshSharesAsync();
        try
        {
            var friends = await _app.Axo.GetFriendsAsync();
            FriendsList.ItemsSource = friends;
            var playing = friends.Count(f => f.Playing);
            Header.Text = playing > 0 ? $"Freunde ({playing} im Spiel)" : "Freunde";
            ShowInfo(friends.Count == 0
                ? "Noch keine Freunde. Füge sie über + mit ihrem Minecraft-Namen hinzu. Sie müssen AxoClient nutzen " +
                  "und dich ebenfalls hinzufügen."
                : null);
        }
        catch (Exception ex)
        {
            ShowInfo("Freunde konnten nicht geladen werden: " + ErrorReport.Short(ex));
        }
        finally
        {
            _loadingFriends = false;
        }
    }

    private async Task RefreshSharesAsync()
    {
        if (_loadingShares)
            return;
        _loadingShares = true;
        try
        {
            var shares = await _app.Axo.GetInboxAsync();
            SharesList.ItemsSource = shares;
            SharesHeader.Text = shares.Count == 1 ? "Geteilt mit dir (1 neu)" : $"Geteilt mit dir ({shares.Count} neu)";
            Ui.Show(SharesPanel, shares.Count > 0);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Geteilte Sachen abrufen", ex);
            SharesPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _loadingShares = false;
        }
    }

    private void ShowInfo(string? text)
    {
        InfoText.Text = text;
        Ui.Show(InfoText, text != null);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = Ui.Input();
        var form = Ui.Stack(
            Ui.Note("Minecraft-Name deines Freundes. Er muss AxoClient nutzen und dich ebenfalls hinzufügen; " +
                    "erst dann seht ihr gegenseitig, auf welchem Server ihr spielt."),
            nameBox);
        if (!await _app.Dialogs.ShowFormAsync("Freund hinzufügen", form, "Hinzufügen", () => nameBox.Text.Trim().Length > 0))
            return;
        await RunAsync(async () => _home.ShowStatus($"{await _app.Axo.AddFriendAsync(nameBox.Text.Trim())} hinzugefügt."));
    }

    private async void Accept_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _app.Axo.AddFriendAsync(Ui.DataOf<FriendInfo>(sender).Name));

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var friend = Ui.DataOf<FriendInfo>(sender);
        if (friend.IsFriend
            && !await _app.Dialogs.ConfirmAsync("Freund entfernen", $"{friend.Name} wirklich entfernen?", "Entfernen", danger: true))
            return;
        await RunAsync(() => _app.Axo.RemoveFriendAsync(friend.Uuid));
    }

    private async void Join_Click(object sender, RoutedEventArgs e)
    {
        var friend = Ui.DataOf<FriendInfo>(sender);
        if (friend.Server != null)
            await _home.JoinServerAsync(friend.Server, friend.Version, friend.Name, ask: false);
    }

    private async void ShareWithFriend_Click(object sender, RoutedEventArgs e) =>
        await ShareDialogs.ShareInstanceAsync(_app, null, Ui.DataOf<FriendInfo>(sender).Uuid);

    private async void OpenShare_Click(object sender, RoutedEventArgs e)
    {
        if (await ShareDialogs.OpenShareAsync(_app, Ui.DataOf<ShareInfo>(sender)))
            await RefreshSharesAsync();
    }

    private async void DismissShare_Click(object sender, RoutedEventArgs e)
    {
        var share = Ui.DataOf<ShareInfo>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Paket verwerfen",
                $"\"{share.Title}\" von {share.FromName} verwerfen?\n\nDein Freund kann es dir danach erneut schicken.",
                "Verwerfen", danger: true))
            return;
        try
        {
            await _app.Axo.DeleteShareAsync(share.Id);
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Verwerfen fehlgeschlagen", ex);
        }
        await RefreshSharesAsync();
    }

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
