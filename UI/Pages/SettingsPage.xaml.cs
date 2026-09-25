using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AxoClient.UI.Pages;

public partial class SettingsPage : UserControl
{
    private const string AutomaticJava = "Automatisch – die von Minecraft mitgelieferte Laufzeit (empfohlen)";

    private AppServices _app = null!;
    private bool _loading;
    private bool _javaLoading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    public void Initialize(AppServices app)
    {
        _app = app;
        var s = app.Settings;
        _loading = true;

        RamSlider.Maximum = RamAdvisor.SliderMaximum();
        RamSlider.Value = Math.Clamp(s.MaxRamMb, (int)RamSlider.Minimum, (int)RamSlider.Maximum);
        RamHint.Text = $"Dein PC hat {RamAdvisor.TotalMb() / 1024.0:0.#} GB. Für Vanilla reichen 2–4 GB, " +
                       "große Modpacks brauchen oft 6–8 GB. Lass etwas für Windows übrig.";

        JvmArgsBox.Text = s.JvmArguments;
        PreLaunchCheckBox.IsChecked = s.PreLaunchCheck;
        Ui.FillPresetChoices(PresetButtons, "JvmPreset", JvmPresets.Get(s.JvmPreset).Id, Preset_Checked);
        UpdatePresetHint();
        FullScreenCheck.IsChecked = s.FullScreen;
        MaximizeCheck.IsChecked = s.MaximizeOnLaunch;
        WidthBox.Text = s.GameWidth > 0 ? s.GameWidth.ToString() : "";
        HeightBox.Text = s.GameHeight > 0 ? s.GameHeight.ToString() : "";
        DiscordCheck.IsChecked = s.DiscordEnabled;
        BadgeCheck.IsChecked = s.BadgeEnabled;

        AutostartCheck.IsEnabled = Autostart.IsAvailable;
        AutostartCheck.IsChecked = Autostart.IsEnabled;
        AutostartMinimizedCheck.IsChecked = s.AutostartMinimized;
        AutostartMinimizedCheck.IsEnabled = AutostartCheck.IsChecked == true;
        if (!Autostart.IsAvailable)
            AutostartHint.Text = "Nur mit der AxoClient.exe möglich, nicht wenn der Launcher über „dotnet“ läuft.";
        foreach (var radio in new[] { AfterKeepOpen, AfterMinimize, AfterHide, AfterClose })
            radio.IsChecked = (string)radio.Tag == s.AfterLaunch.ToString();
        UpdateAfterLaunchHint();
        LauncherDirText.Text = AppPaths.LauncherDir;
        _loading = false;
        UpdateRamText();
        _ = LoadJavaAsync();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (GeneralView == null)
            return;
        Ui.Show(GeneralView, GeneralTab.IsChecked == true);
        Ui.Show(GameView, GameTab.IsChecked == true);
        Ui.Show(JavaView, JavaTab.IsChecked == true);
        Ui.Show(OnlineView, OnlineTab.IsChecked == true);
        Ui.Show(AdvancedView, AdvancedTab.IsChecked == true);
        Scroller.ScrollToTop();
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
        AutostartMinimizedCheck.IsEnabled = AutostartCheck.IsChecked == true;
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
        AfterLaunchAction.KeepOpen => "Der Launcher bleibt, wie er ist.",
        AfterLaunchAction.Minimize => "Der Launcher wandert in die Taskleiste.",
        AfterLaunchAction.Hide => "Der Launcher verschwindet ganz (Symbol unten rechts im Infobereich) und kommt " +
                                  "von selbst zurück, sobald du das Spiel beendest. Discord-Status und Absturzhilfe laufen weiter.",
        AfterLaunchAction.Close => "Der Launcher beendet sich, das Spiel läuft allein weiter. Dann gibt es keinen " +
                                   "Discord-Status, keine Spielzeit-Zählung und keine Hilfe bei Abstürzen.",
        _ => ""
    };

    private void Preset_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _app == null || ((RadioButton)sender).Tag is not string id)
            return;
        _app.Settings.JvmPreset = id;
        _app.SaveSettings();
        UpdatePresetHint();
    }

    private void UpdatePresetHint()
    {
        var preset = JvmPresets.Get(_app.Settings.JvmPreset);
        PresetHint.Text = preset.Description;
        JvmArgsHint.Text = preset.Id == JvmPresets.CustomId
            ? "Für Fortgeschrittene, z.B. -XX:+UseG1GC. Leer lassen, wenn du unsicher bist."
            : "Wird zusätzlich zum Preset übergeben. Normalerweise bleibt das Feld leer.";
    }

    private async Task LoadJavaAsync(bool rescan = false)
    {
        if (_javaLoading)
            return;
        _javaLoading = true;
        JavaRescanButton.IsEnabled = false;
        JavaHint.Text = "Suche nach Java-Installationen...";
        try
        {
            var found = await JavaChoice.FindAsync(rescan);
            var choices = JavaChoice.Build(AutomaticJava, found, _app.Settings.JavaPath);
            _loading = true;
            JavaBox.ItemsSource = choices;
            JavaBox.SelectedItem = JavaChoice.Find(choices, _app.Settings.JavaPath);
            _loading = false;
            UpdateJavaHint(found.Count);
        }
        catch (Exception ex)
        {
            JavaHint.Text = "Die Java-Suche ist fehlgeschlagen: " + ErrorReport.Short(ex);
        }
        finally
        {
            JavaRescanButton.IsEnabled = true;
            _javaLoading = false;
        }
    }

    private void UpdateJavaHint(int foundCount)
    {
        var chosen = _app.Settings.JavaPath;
        JavaHint.Text = string.IsNullOrEmpty(chosen)
            ? $"{foundCount} Java-Installation(en) gefunden. Es wird jeweils die Laufzeit genutzt, " +
              "die Mojang für die Minecraft-Version vorsieht – bei Bedarf lädt der Launcher sie herunter."
            : File.Exists(chosen)
                ? $"Alle Instanzen starten mit: {chosen}\nPasst es nicht zur Minecraft-Version, meldet sich die Prüfung vor dem Start."
                : $"Achtung: \"{chosen}\" gibt es nicht (mehr). Stelle wieder \"Automatisch\" ein.";
    }

    private void JavaBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _app == null || JavaBox.SelectedItem is not JavaChoice choice)
            return;
        _app.Settings.JavaPath = choice.Runtime?.Path;
        _app.SaveSettings();
        UpdateJavaHint(JavaBox.Items.Count - 1);
    }

    private async void JavaRescan_Click(object sender, RoutedEventArgs e) => await LoadJavaAsync(rescan: true);

    private async void JavaBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "javaw.exe auswählen",
            Filter = "Java (javaw.exe;java.exe)|javaw.exe;java.exe|Alle Dateien|*.*"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var major = await Task.Run(() => JavaRuntimes.ReadMajor(dialog.FileName));
        if (major == 0 && !await _app.Dialogs.ConfirmAsync("Java nicht erkannt",
                $"Aus \"{dialog.FileName}\" ließ sich keine Java-Version lesen. Trotzdem verwenden?", "Verwenden"))
            return;

        _app.Settings.JavaPath = dialog.FileName;
        _app.SaveSettings();
        await LoadJavaAsync(rescan: true);
    }

    private void JavaFolder_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(AppPaths.Runtime, create: true);

    private void UpdateRamText() => RamValue.Text = $"{RamSlider.Value / 1024:0.#} GB";

    private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _app == null)
            return;
        UpdateRamText();
        _app.Settings.MaxRamMb = (int)RamSlider.Value;
        _app.SaveSettings();
    }

    private void Check_Click(object sender, RoutedEventArgs e) => SaveAll();

    private void Text_LostFocus(object sender, RoutedEventArgs e) => SaveAll();

    private void Number_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

    private void SaveAll()
    {
        if (_loading)
            return;
        var s = _app.Settings;
        s.JvmArguments = JvmArgsBox.Text.Trim();
        s.PreLaunchCheck = PreLaunchCheckBox.IsChecked == true;
        s.FullScreen = FullScreenCheck.IsChecked == true;
        s.MaximizeOnLaunch = MaximizeCheck.IsChecked == true;
        s.GameWidth = int.TryParse(WidthBox.Text, out var w) ? w : 0;
        s.GameHeight = int.TryParse(HeightBox.Text, out var h) ? h : 0;
        s.AutostartMinimized = AutostartMinimizedCheck.IsChecked == true;
        s.DiscordEnabled = DiscordCheck.IsChecked == true;
        s.BadgeEnabled = BadgeCheck.IsChecked == true;
        _app.SaveSettings();
    }

    private void OpenLauncherFolder_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(AppPaths.LauncherDir);

    private async void OpenErrorLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(ErrorReport.LogPath))
        {
            Shell.OpenFile(ErrorReport.LogPath);
            return;
        }
        await _app.Dialogs.ShowMessageAsync("Kein Fehlerprotokoll",
            "Seit der Installation ist nichts schiefgegangen, was festgehalten wurde.\n\n" +
            $"Sobald etwas passiert, steht es hier: {ErrorReport.LogPath}");
    }
}
