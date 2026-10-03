using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AxoClient.UI.Pages;

public partial class SettingsPage : UserControl
{
    private AppServices _app = null!;
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    public void Initialize(AppServices app)
    {
        _app = app;
        var s = app.Settings;
        _loading = true;

        AutostartCheck.IsEnabled = Autostart.IsAvailable;
        AutostartCheck.IsChecked = Autostart.IsEnabled;
        if (!Autostart.IsAvailable)
            AutostartHint.Text = "Nur mit der AxoClient.exe möglich, nicht wenn der Launcher über „dotnet“ läuft";
        foreach (var radio in new[] { AfterKeepOpen, AfterMinimize, AfterHide, AfterClose })
            radio.IsChecked = (string)radio.Tag == s.AfterLaunch.ToString();
        UpdateAfterLaunchHint();

        ModeFullscreen.IsChecked = s.FullScreen;
        ModeMaximized.IsChecked = !s.FullScreen && s.MaximizeOnLaunch;
        ModeWindow.IsChecked = !s.FullScreen && !s.MaximizeOnLaunch;
        WidthBox.Text = (s.GameWidth > 0 ? s.GameWidth : 1280).ToString();
        HeightBox.Text = (s.GameHeight > 0 ? s.GameHeight : 720).ToString();
        PreLaunchCheckBox.IsChecked = s.PreLaunchCheck;
        DiscordCheck.IsChecked = s.DiscordEnabled;
        RelayCheck.IsChecked = s.JoinRelayEnabled;
        BadgeCheck.IsChecked = s.BadgeEnabled;
        _loading = false;

        UpdateSizeRow();
        UpdatePreview();
        app.Accounts.Changed += () => Dispatcher.InvokeAsync(UpdatePreview);
    }

    private async void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Autostart.SetEnabled(AutostartCheck.IsChecked == true);
        }
        catch (Exception ex)
        {
            AutostartCheck.IsChecked = Autostart.IsEnabled;
            await _app.Dialogs.ShowErrorAsync("Autostart konnte nicht geändert werden", ex);
        }
    }

    private void AfterLaunch_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _app == null || ((RadioButton)sender).Tag is not string tag
            || !Enum.TryParse<AfterLaunchAction>(tag, out var action))
            return;
        _app.Settings.AfterLaunch = action;
        _app.SaveSettings();
        UpdateAfterLaunchHint();
    }

    private void UpdateAfterLaunchHint() => AfterLaunchHint.Text = _app.Settings.AfterLaunch switch
    {
        AfterLaunchAction.KeepOpen => "Der Launcher bleibt geöffnet, während du spielst",
        AfterLaunchAction.Minimize => "Der Launcher wird in die Taskleiste minimiert",
        AfterLaunchAction.Hide => "Der Launcher verschwindet und kommt nach dem Spiel zurück",
        AfterLaunchAction.Close => "Der Launcher wird beendet, sobald das Spiel läuft – ohne Discord-Status und Absturzhilfe",
        _ => ""
    };

    private void WindowMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _app == null)
            return;
        _app.Settings.FullScreen = ModeFullscreen.IsChecked == true;
        _app.Settings.MaximizeOnLaunch = ModeMaximized.IsChecked == true;
        _app.SaveSettings();
        UpdateSizeRow();
    }

    private void UpdateSizeRow()
    {
        var window = ModeWindow.IsChecked == true;
        SizeRow.Opacity = window ? 1 : 0.45;
        SizeRow.IsEnabled = window;
        SizeHint.Text = window ? "Startgröße des Spielfensters" : "Nur im Fenstermodus einstellbar";
        var size = $"{WidthBox.Text}x{HeightBox.Text}";
        foreach (var preset in new[] { Preset720, Preset900, Preset1080 })
            preset.IsChecked = (string)preset.Tag == size;
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((string)((RadioButton)sender).Tag).Split('x');
        _loading = true;
        WidthBox.Text = parts[0];
        HeightBox.Text = parts[1];
        _loading = false;
        SaveSize();
    }

    private void Size_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && _app != null)
            UpdateSizeRow();
    }

    private void Size_LostFocus(object sender, RoutedEventArgs e) => SaveSize();

    private void SaveSize()
    {
        if (_app == null)
            return;
        var width = int.TryParse(WidthBox.Text, out var w) ? Math.Clamp(w, 640, 7680) : 1280;
        var height = int.TryParse(HeightBox.Text, out var h) ? Math.Clamp(h, 480, 4320) : 720;
        _loading = true;
        WidthBox.Text = width.ToString();
        HeightBox.Text = height.ToString();
        _loading = false;
        _app.Settings.GameWidth = width;
        _app.Settings.GameHeight = height;
        _app.SaveSettings();
        UpdateSizeRow();
    }

    private void Number_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _app.Settings.PreLaunchCheck = PreLaunchCheckBox.IsChecked == true;
        _app.Settings.DiscordEnabled = DiscordCheck.IsChecked == true;
        _app.Settings.JoinRelayEnabled = RelayCheck.IsChecked == true;
        _app.SaveSettings();
    }

    private void Badge_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _app.Settings.BadgeEnabled = BadgeCheck.IsChecked == true;
        _app.SaveSettings();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        PreviewName.Text = _app.Accounts.Session?.Username ?? "Du";
        PreviewHead.Source = _app.Accounts.Profile?.Head;
        PreviewBadge.Visibility = BadgeCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }
}
