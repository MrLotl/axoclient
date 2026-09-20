using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace McLauncher.Pages;

/// <summary>Launcher- und Spieleinstellungen; jede Änderung wird sofort gespeichert.</summary>
public partial class SettingsPage : UserControl
{
    private AppState _app = null!;
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    public void Initialize(AppState app)
    {
        _app = app;
        var s = app.Settings;
        _loading = true;

        // Obergrenze: gesamter Arbeitsspeicher des PCs
        var totalMb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
        RamSlider.Maximum = Math.Max(2048, totalMb / 512 * 512);
        RamSlider.Value = Math.Clamp(s.MaxRamMb, (int)RamSlider.Minimum, (int)RamSlider.Maximum);
        RamHint.Text = $"Dein PC hat {totalMb / 1024.0:0.#} GB. Für Vanilla reichen 2–4 GB, " +
                       "große Modpacks brauchen oft 6–8 GB. Lass etwas für Windows übrig.";

        JvmArgsBox.Text = s.JvmArguments;
        FullScreenCheck.IsChecked = s.FullScreen;
        MaximizeCheck.IsChecked = s.MaximizeOnLaunch;
        WidthBox.Text = s.GameWidth > 0 ? s.GameWidth.ToString() : "";
        HeightBox.Text = s.GameHeight > 0 ? s.GameHeight.ToString() : "";
        MinimizeCheck.IsChecked = s.MinimizeOnLaunch;
        DiscordCheck.IsChecked = s.DiscordEnabled;
        BadgeCheck.IsChecked = s.BadgeEnabled;
        _loading = false;
        UpdateRamText();
    }

    private void UpdateRamText() => RamValue.Text = $"{RamSlider.Value / 1024:0.#} GB";

    private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _app == null)
            return;
        UpdateRamText();
        _app.Settings.MaxRamMb = (int)RamSlider.Value;
        _app.Save();
    }

    private void Check_Click(object sender, RoutedEventArgs e) => SaveAll();

    private void Text_LostFocus(object sender, RoutedEventArgs e) => SaveAll();

    private void Number_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void SaveAll()
    {
        if (_loading)
            return;
        var s = _app.Settings;
        s.JvmArguments = JvmArgsBox.Text.Trim();
        s.FullScreen = FullScreenCheck.IsChecked == true;
        s.MaximizeOnLaunch = MaximizeCheck.IsChecked == true;
        s.GameWidth = int.TryParse(WidthBox.Text, out var w) ? w : 0;
        s.GameHeight = int.TryParse(HeightBox.Text, out var h) ? h : 0;
        s.MinimizeOnLaunch = MinimizeCheck.IsChecked == true;
        s.DiscordEnabled = DiscordCheck.IsChecked == true;
        s.BadgeEnabled = BadgeCheck.IsChecked == true;
        _app.Save();
    }

    private void OpenLauncherFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppState.LauncherDir}\"") { UseShellExecute = true });
}
