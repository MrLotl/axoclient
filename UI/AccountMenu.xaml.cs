using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AxoClient.UI;

public partial class AccountMenu : UserControl
{
    private AppServices _app = null!;

    public AccountMenu()
    {
        InitializeComponent();
    }

    public event Action? CloseRequested;

    public void Initialize(AppServices app)
    {
        _app = app;
        _app.Accounts.Changed += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        var accounts = _app.Accounts;
        AccountList.ItemsSource = accounts.Saved;
        LogoutText.Text = accounts.Session is { } session ? session.Username + " abmelden" : "Abmelden";
        Ui.Show(LogoutButton, accounts.Session != null);
        var problem = accounts.Problem;
        ProblemText.Text = problem == null ? "" : ErrorReport.Short(problem.Error);
        ProblemText.ToolTip = problem == null ? null : problem.What + ": Klicken für Einzelheiten";
        Ui.Show(ProblemText, problem != null);
    }

    private async void Account_Click(object sender, RoutedEventArgs e)
    {
        var account = Ui.DataOf<SavedAccount>(sender);
        CloseRequested?.Invoke();
        if (account.Active)
            return;
        await UiRun.GuardAsync(_app, "Konto konnte nicht gewechselt werden", () => _app.Accounts.SwitchAsync(account.Id));
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
        await UiRun.GuardAsync(_app, "Anmeldung fehlgeschlagen", _app.Accounts.LoginAsync);
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
        if (_app.Accounts.Session is not { } session)
            return;
        if (!await _app.Dialogs.ConfirmAsync("Abmelden", $"{session.Username} von diesem PC abmelden?", "Abmelden", danger: true))
            return;
        await UiRun.GuardAsync(_app, "Abmelden fehlgeschlagen", _app.Accounts.LogoutAsync);
    }

    private async void Problem_Click(object sender, MouseButtonEventArgs e)
    {
        CloseRequested?.Invoke();
        if (_app.Accounts.Problem is { } problem)
            await _app.Dialogs.ShowErrorAsync(problem.What, problem.Error);
    }
}
