using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace McLauncher.Pages;

/// <summary>Startseite, rechte Spalte: Freunde (AxoClient-Nutzer) und Server-Beitritt.</summary>
public partial class HomePage
{
    /// <summary>Anmeldung beim AxoClient-Dienst ("zuletzt gesehen" für das Tabliste-Symbol); Fehler stören den Start nicht.</summary>
    private async Task RegisterQuietlyAsync()
    {
        if (!_app.Friends.Available)
            return;
        try
        {
            await _app.Friends.RegisterAsync();
        }
        catch
        {
            // Dienst nicht erreichbar: dann eben ohne neues "zuletzt gesehen"
        }
    }

    // ================= Server beitreten =================

    /// <summary>
    /// Startet eine Instanz und verbindet mit dem Server. Bevorzugt die ausgewählte Instanz, sonst eine mit
    /// passender Minecraft-Version. <paramref name="ask"/>: vorher nachfragen (z.B. wenn der Beitritt aus Discord kommt).
    /// </summary>
    public async Task JoinServerAsync(string server, string? version, string? friendName, bool ask)
    {
        if (_busy)
            return;
        if (_app.Session == null)
        {
            await _app.Dialogs.ShowMessageAsync("Nicht angemeldet", "Bitte melde dich links unten an, um beizutreten.");
            return;
        }

        var inst = _app.SelectedInstallation is { } selected && (version == null || selected.MinecraftVersion == version)
            ? selected
            : _app.Settings.Installations.Where(i => i.MinecraftVersion == version)
                  .OrderByDescending(i => Badge.IsActiveFor(i, _app.Settings))
                  .FirstOrDefault()
              ?? _app.SelectedInstallation;
        if (inst == null)
            return;

        var who = friendName != null ? $"{friendName} spielt" : "Dort wird";
        var mismatch = version != null && inst.MinecraftVersion != version
            ? $"\n\nAchtung: {who} mit Minecraft {version} gespielt, du hast keine Instanz mit dieser Version. " +
              $"Es wird \"{inst.Name}\" ({inst.MinecraftVersion}) verwendet."
            : "";
        if ((ask || mismatch.Length > 0)
            && !await _app.Dialogs.ConfirmAsync("Server beitreten",
                $"Mit \"{inst.Name}\" auf {server} beitreten?{mismatch}", "Beitreten"))
            return;

        _app.SelectInstallation(inst);
        await PlayAsync(new QuickPlay(Server: server));
    }

    // ================= Freunde =================

    private async Task RefreshFriendsAsync()
    {
        if (_app == null || !IsVisible || _loadingFriends)
            return;

        var available = _app.Friends.Available;
        AddFriendButton.IsEnabled = RefreshFriendsButton.IsEnabled = available;
        if (!available)
        {
            FriendsList.ItemsSource = null;
            FriendsHeader.Text = "Freunde";
            ShowFriendsInfo(_app.Session == null
                ? "Melde dich an, um deine Freunde zu sehen."
                : "Schalte unter Einstellungen \"Axolotl-Symbol in der Tabliste\" ein, um Freunde hinzuzufügen " +
                  "und ihnen auf Server zu folgen.");
            return;
        }

        _loadingFriends = true;
        try
        {
            var friends = await _app.Friends.GetFriendsAsync();
            FriendsList.ItemsSource = friends;
            var playing = friends.Count(f => f.Playing);
            FriendsHeader.Text = playing > 0 ? $"Freunde ({playing} im Spiel)" : "Freunde";
            ShowFriendsInfo(friends.Count == 0
                ? "Noch keine Freunde. Füge sie über + mit ihrem Minecraft-Namen hinzu. Sie müssen AxoClient nutzen " +
                  "und dich ebenfalls hinzufügen."
                : null);
        }
        catch (Exception ex)
        {
            ShowFriendsInfo("Freunde konnten nicht geladen werden: " + ex.Message);
        }
        finally
        {
            _loadingFriends = false;
        }
    }

    private void ShowFriendsInfo(string? text)
    {
        FriendsInfo.Text = text;
        FriendsInfo.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshFriends_Click(object sender, RoutedEventArgs e) => _ = RefreshFriendsAsync();

    private async void AddFriend_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Style = (Style)FindResource("LauncherTextBox") };
        var form = new StackPanel();
        form.Children.Add(new TextBlock
        {
            Text = "Minecraft-Name deines Freundes. Er muss AxoClient nutzen und dich ebenfalls hinzufügen; " +
                   "erst dann seht ihr gegenseitig, auf welchem Server ihr spielt.",
            Foreground = (Brush)FindResource("SubtleText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        form.Children.Add(nameBox);
        nameBox.Loaded += (_, _) => nameBox.Focus();

        if (!await _app.Dialogs.ShowFormAsync("Freund hinzufügen", form, "Hinzufügen",
                () => nameBox.Text.Trim().Length > 0))
            return;
        await RunFriendActionAsync(async () =>
        {
            var name = await _app.Friends.AddAsync(nameBox.Text.Trim());
            StatusText.Text = $"{name} hinzugefügt.";
        });
    }

    private async void AcceptFriend_Click(object sender, RoutedEventArgs e)
    {
        var friend = (FriendInfo)((FrameworkElement)sender).DataContext;
        await RunFriendActionAsync(() => _app.Friends.AddAsync(friend.Name));
    }

    private async void RemoveFriend_Click(object sender, RoutedEventArgs e)
    {
        var friend = (FriendInfo)((FrameworkElement)sender).DataContext;
        if (friend.State == "friend"
            && !await _app.Dialogs.ConfirmAsync("Freund entfernen", $"{friend.Name} wirklich entfernen?", "Entfernen", danger: true))
            return;
        await RunFriendActionAsync(() => _app.Friends.RemoveAsync(friend.Uuid));
    }

    private async void JoinFriend_Click(object sender, RoutedEventArgs e)
    {
        var friend = (FriendInfo)((FrameworkElement)sender).DataContext;
        if (friend.Server != null)
            await JoinServerAsync(friend.Server, friend.Version, friend.Name, ask: false);
    }

    private async Task RunFriendActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowMessageAsync("Freunde", ex.Message);
        }
        await RefreshFriendsAsync();
    }
}
