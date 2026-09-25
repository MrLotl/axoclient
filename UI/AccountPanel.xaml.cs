using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AxoClient.UI;

public partial class AccountPanel : UserControl
{
    private AppServices _app = null!;

    public AccountPanel()
    {
        InitializeComponent();
    }

    public void Initialize(AppServices app)
    {
        _app = app;
        _app.Accounts.Changed += Refresh;
    }

    public async Task RestoreSessionAsync()
    {
        AccountLink.IsEnabled = false;
        AccountLinkText.Text = "Prüfe Anmeldung...";
        await _app.Accounts.TryRestoreAsync();
        AccountLink.IsEnabled = true;
        Refresh();
    }

    private void Refresh()
    {
        var accounts = _app.Accounts;
        AccountName.Text = accounts.Session?.Username ?? "Nicht angemeldet";
        AccountLinkText.Text = accounts.Session == null ? "Mit Microsoft anmelden" : "Abmelden";
        AvatarImage.Source = accounts.Profile?.Head;

        var problem = accounts.Problem;
        ProblemText.Text = problem == null ? "" : ErrorReport.Short(problem.Error);
        ProblemText.ToolTip = problem == null ? null : problem.What + ": Klicken für Einzelheiten";
        Ui.Show(ProblemText, problem != null);
    }

    private async void Problem_Click(object sender, MouseButtonEventArgs e)
    {
        if (_app.Accounts.Problem is { } problem)
            await _app.Dialogs.ShowErrorAsync(problem.What, problem.Error);
    }

    private async void AccountLink_Click(object sender, RoutedEventArgs e)
    {
        AccountLink.IsEnabled = false;
        try
        {
            if (_app.Accounts.Session == null)
            {
                AccountLinkText.Text = "Anmeldung läuft...";
                await _app.Accounts.LoginAsync();
            }
            else if (await _app.Dialogs.ConfirmAsync("Abmelden", "Vom Microsoft-Konto abmelden?", "Abmelden"))
            {
                await _app.Accounts.LogoutAsync();
            }
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Anmeldung fehlgeschlagen", ex);
        }
        finally
        {
            AccountLink.IsEnabled = true;
            Refresh();
        }
    }
}
