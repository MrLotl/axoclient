using System.Windows;

namespace McLauncher.Pages;

/// <summary>Startseite, rechte Spalte: Postfach mit dem, was Freunde geschickt haben.</summary>
public partial class HomePage
{
    private bool _loadingShares;

    /// <summary>
    /// Holt das Postfach. Fehler werden hier bewusst nicht gemeldet: Der Dienst kennt das Teilen vielleicht noch nicht
    /// (alter Worker), dann bleibt das Postfach einfach ausgeblendet. Beim ersten Senden erscheint die Erklärung dazu.
    /// </summary>
    private async Task RefreshSharesAsync()
    {
        if (_app == null || _loadingShares)
            return;
        if (!_app.Friends.Available)
        {
            SharesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        _loadingShares = true;
        try
        {
            var shares = await _app.Friends.GetInboxAsync();
            SharesList.ItemsSource = shares;
            SharesHeader.Text = shares.Count == 1 ? "Geteilt mit dir (1 neu)" : $"Geteilt mit dir ({shares.Count} neu)";
            SharesPanel.Visibility = shares.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            SharesPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _loadingShares = false;
        }
    }

    private async void OpenShare_Click(object sender, RoutedEventArgs e)
    {
        var share = (ShareInfo)((FrameworkElement)sender).DataContext;
        if (await ShareUi.OpenShareAsync(_app, share))
            await RefreshSharesAsync();
    }

    private async void DismissShare_Click(object sender, RoutedEventArgs e)
    {
        var share = (ShareInfo)((FrameworkElement)sender).DataContext;
        if (!await _app.Dialogs.ConfirmAsync("Paket verwerfen",
                $"\"{share.Title}\" von {share.FromName} verwerfen?\n\nDein Freund kann es dir danach erneut schicken.",
                "Verwerfen", danger: true))
            return;
        try
        {
            await _app.Friends.DeleteShareAsync(share.Id);
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowMessageAsync("Verwerfen fehlgeschlagen", ex.Message);
        }
        await RefreshSharesAsync();
    }

    /// <summary>Teilen-Dialog von der Freundesliste aus, mit diesem Freund schon angekreuzt.</summary>
    private async void ShareWithFriend_Click(object sender, RoutedEventArgs e)
    {
        var friend = (FriendInfo)((FrameworkElement)sender).DataContext;
        await ShareUi.ShareInstanceAsync(_app, null, friend.Uuid);
    }
}
