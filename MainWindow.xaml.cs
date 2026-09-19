using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace McLauncher;

public partial class MainWindow : Window, IDialogService
{
    private readonly AppState _app;
    private TaskCompletionSource<bool>? _dialogResult;
    private Func<bool>? _dialogValidate;

    /// <summary>Fertig, sobald die gespeicherte Anmeldung wiederhergestellt wurde (oder das fehlschlug).</summary>
    private readonly TaskCompletionSource _sessionRestored = new();

    public MainWindow()
    {
        InitializeComponent();
        _app = new AppState(this);
        _app.AccountChanged += UpdateAccount;

        HomePage.Initialize(_app);
        InstancesPage.Initialize(_app);
        SkinsPage.Initialize(_app);
        SettingsPage.Initialize(_app);

        InstancesPage.PlayRequested += (inst, quickPlay) =>
        {
            _app.SelectInstallation(inst);
            NavHome.IsChecked = true;
            _ = HomePage.PlayAsync(quickPlay);
        };

        // "Beitreten" in Discord: Discord startet bzw. benachrichtigt AxoClient
        _app.Discord.JoinRequested += (server, version) => Dispatcher.InvokeAsync(() =>
        {
            NavHome.IsChecked = true;
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            _ = JoinFromDiscordAsync(server, version);
        });
        _app.Discord.Initialize(_app.Settings);
    }

    private async Task JoinFromDiscordAsync(string server, string? version)
    {
        await _sessionRestored.Task; // startet Discord den Launcher, kommt der Beitritt vor der Anmeldung an
        await HomePage.JoinServerAsync(server, version, null, ask: true);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = "Version " + Updater.CurrentVersion;
        Updater.CleanUp();
        var updateCheck = CheckForUpdateAsync();

        // Gespeichertes Konto still wiederherstellen, ohne Login-Fenster
        AccountLink.IsEnabled = false;
        AccountLinkText.Text = "Prüfe Anmeldung...";
        await _app.TryRestoreSessionAsync();
        _sessionRestored.TrySetResult();
        AccountLink.IsEnabled = true;
        UpdateAccount();
        await updateCheck;
    }

    /// <summary>Fragt bei jedem Start auf GitHub nach einer neueren Version und bietet die Installation an.</summary>
    private async Task CheckForUpdateAsync()
    {
        UpdateInfo? update;
        try
        {
            update = await Updater.CheckAsync(_app.Http);
        }
        catch
        {
            return; // offline oder GitHub nicht erreichbar: einfach ohne Update weiter
        }
        if (update == null)
            return;

        var notes = update.Notes.Trim();
        if (notes.Length > 600)
            notes = notes[..600] + " ...";
        if (!await ConfirmAsync("Update verfügbar",
                $"AxoClient {update.Version} ist verfügbar (du hast {Updater.CurrentVersion})." +
                (notes.Length > 0 ? "\n\n" + notes : "") +
                "\n\nJetzt installieren? AxoClient startet danach neu; laufende Spiele bleiben offen.",
                "Installieren"))
            return;

        try
        {
            NavHome.IsChecked = true;
            var progress = new Progress<double>(p => HomePage.StatusText.Text = $"Update wird geladen... {p:0} %");
            await Updater.InstallAsync(_app.Http, update, progress);
            Close();
        }
        catch (Exception ex)
        {
            HomePage.StatusText.Text = "";
            await ShowMessageAsync("Update fehlgeschlagen",
                $"{ex.Message}\n\nDu kannst die neue Version auch selbst herunterladen:\n{update.PageUrl}");
        }
    }

    // ---------- Eigene Titelleiste ----------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        // Dunkler Fensterrahmen/Schatten und abgerundete Ecken (Windows 11); auf älteren Systemen wirkungslos
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
        var round = 2;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));  // DWMWA_WINDOW_CORNER_PREFERENCE = rund
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        // Ohne Standard-Titelleiste ragt ein maximiertes Fenster um den Rahmen über den Bildschirm hinaus
        var frame = SystemParameters.WindowResizeBorderThickness;
        RootGrid.Margin = maximized
            ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4)
            : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Verkleinern" : "Maximieren";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _app.Discord.Dispose(); // Discord-Status entfernen
        base.OnClosed(e);
    }

    // ---------- Navigation ----------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return; // beim Aufbau des Fensters sind die Seiten noch nicht alle erzeugt
        HomePage.Visibility = NavHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        InstancesPage.Visibility = NavInstances.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SkinsPage.Visibility = NavSkins.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (NavInstances.IsChecked == true && sender == NavInstances)
            InstancesPage.ShowList();
        if (NavSettings.IsChecked == true)
            SettingsPage.Initialize(_app); // z.B. neuen CurseForge-Schlüssel aus der Mod-Suche übernehmen
    }

    // ---------- Konto ----------

    private void UpdateAccount()
    {
        AccountName.Text = _app.Session?.Username ?? "Nicht angemeldet";
        AccountLinkText.Text = _app.Session == null ? "Mit Microsoft anmelden" : "Abmelden";
        AvatarImage.Source = _app.Profile?.Head;
    }

    private async void AccountLink_Click(object sender, RoutedEventArgs e)
    {
        AccountLink.IsEnabled = false;
        try
        {
            if (_app.Session == null)
            {
                AccountLinkText.Text = "Anmeldung läuft...";
                await _app.LoginAsync();
            }
            else if (await ConfirmAsync("Abmelden", "Vom Microsoft-Konto abmelden?", "Abmelden"))
            {
                await _app.LogoutAsync();
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Anmeldung fehlgeschlagen", ex.Message);
        }
        finally
        {
            AccountLink.IsEnabled = true;
            UpdateAccount();
        }
    }

    // ---------- Eingeblendete Dialoge ----------

    public Task<bool> ConfirmAsync(string title, string text, string confirmText = "OK", bool danger = false) =>
        ShowDialog(title, text, confirmText, showCancel: true, danger);

    public Task ShowMessageAsync(string title, string text) =>
        ShowDialog(title, text, "OK", showCancel: false, danger: false);

    public Task<bool> ShowFormAsync(string title, FrameworkElement content, string confirmText, Func<bool>? validate = null)
    {
        var task = ShowDialog(title, null, confirmText, showCancel: true, danger: false, content, validate);
        // Erstes Eingabefeld direkt fokussieren
        Dispatcher.BeginInvoke(() => FindFirstTextBox(content)?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        return task;
    }

    private static System.Windows.Controls.TextBox? FindFirstTextBox(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.TextBox box)
                return box;
            if (FindFirstTextBox(child) is { } found)
                return found;
        }
        return null;
    }

    private Task<bool> ShowDialog(string title, string? text, string confirmText, bool showCancel, bool danger,
        FrameworkElement? content = null, Func<bool>? validate = null)
    {
        _dialogResult?.TrySetResult(false); // höchstens ein Dialog gleichzeitig
        _dialogResult = new TaskCompletionSource<bool>();
        _dialogValidate = validate;

        DialogTitle.Text = title;
        DialogText.Text = text ?? "";
        DialogTextScroller.Visibility = content == null ? Visibility.Visible : Visibility.Collapsed;
        DialogContent.Content = content;
        DialogContent.Visibility = content == null ? Visibility.Collapsed : Visibility.Visible;
        DialogOk.Content = confirmText;
        DialogOk.Background = (System.Windows.Media.Brush)FindResource(danger ? "Danger" : "Accent");
        DialogCancel.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        DialogOverlay.Visibility = Visibility.Visible;
        DialogOk.Focus();
        return _dialogResult.Task;
    }

    private void CloseDialog(bool result)
    {
        // Bestätigen nur, wenn das Formular gültig ist (z.B. Pflichtfeld ausgefüllt)
        if (result && _dialogValidate != null && !_dialogValidate())
            return;

        DialogOverlay.Visibility = Visibility.Collapsed;
        DialogContent.Content = null;
        _dialogValidate = null;
        _dialogResult?.TrySetResult(result);
        _dialogResult = null;
    }

    private void DialogOk_Click(object sender, RoutedEventArgs e) => CloseDialog(true);

    private void DialogCancel_Click(object sender, RoutedEventArgs e) => CloseDialog(false);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Enter = bestätigen, Escape = abbrechen (auch während man in einem Eingabefeld tippt)
        if (DialogOverlay.Visibility == Visibility.Visible && e.Key is Key.Escape or Key.Enter)
        {
            CloseDialog(e.Key == Key.Enter);
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}
