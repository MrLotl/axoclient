using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace McLauncher.Pages;

/// <summary>Startseite einer Instanz: Spielzeit, Inhalte, Speicher-Empfehlung und Crash-Analyse.</summary>
public partial class InstanceOverview : UserControl
{
    private sealed record Stat(string Label, string Value);

    private AppState _app = null!;
    private Installation? _inst;
    private int _request;

    public InstanceOverview()
    {
        InitializeComponent();
    }

    public void Show(AppState app, Installation inst)
    {
        _app = app;
        _inst = inst;
        Refresh();
    }

    /// <summary>Werte neu lesen (z.B. nach Spielende).</summary>
    public async void Refresh()
    {
        if (_inst is not { } inst)
            return;
        var request = ++_request;

        var stats = new List<Stat>
        {
            new("Spielzeit", FormatDuration(inst.PlayTimeSeconds)),
            new("Starts", inst.LaunchCount.ToString()),
            new("Zuletzt gespielt", inst.LastPlayedUtc is { } last ? FormatAgo(last) : "nie"),
            new("Abstürze", inst.CrashCount.ToString())
        };
        StatsList.ItemsSource = stats.ToList();

        var advice = RamAdvisor.Recommend(inst);
        var current = RamAdvisor.CurrentMb(inst, _app.Settings);
        var source = inst.MaxRamMb == null ? "aus den Launcher-Einstellungen" : "nur für diese Instanz";
        if (current == advice.Mb)
        {
            RamText.Text = $"{RamAdvisor.Format(current)} ({source}) passen zu dieser Instanz ({advice.Reason}).";
            RamButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            RamText.Text = $"Aktuell {RamAdvisor.Format(current)} ({source}). Empfohlen: {RamAdvisor.Format(advice.Mb)} " +
                           $"({advice.Reason}).";
            RamButton.Visibility = Visibility.Visible;
            RamButton.IsEnabled = !_app.IsRunning(inst);
        }

        CrashText.Text = inst.LastCrashUtc is { } crash
            ? $"Letzter Absturz {FormatAgo(crash)}. Die Analyse sucht die Ursache und kann sie direkt beheben."
            : "Kein Absturz aufgezeichnet. Die Analyse liest trotzdem das letzte Log, falls etwas nicht stimmt.";

        RefreshProfiles();

        var (mods, worlds, size) = await Task.Run(() => Measure(inst));
        if (request != _request)
            return;
        stats.Add(new Stat("Mods", inst.Loader == LoaderType.Vanilla ? "-" : mods.ToString()));
        stats.Add(new Stat("Welten", worlds.ToString()));
        stats.Add(new Stat("Ordnergröße", FormatSize(size)));
        stats.Add(new Stat("Loader", inst.Loader == LoaderType.Vanilla ? "Vanilla" : inst.Loader.ToString()));
        StatsList.ItemsSource = stats;
    }

    private void RefreshProfiles()
    {
        if (_inst == null)
            return;
        var names = OverlayConfigFile.ProfileNames(_inst);
        ProfileList.ItemsSource = names;
        ProfilesText.Text = names.Count == 0
            ? "Noch keine Profile. Im Spiel (rechte Umschalttaste → Profile) speicherst du deine Anzeigen als Profil, z.B. \"PvP\" oder \"Bauen\". Hier kannst du sie dann an Freunde schicken."
            : "Im Spiel unter \"Profile\" wechselst du zwischen ihnen. Mit \"Teilen\" gehen sie an Freunde oder in eine Datei.";
    }

    private async void ShareProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_inst != null && ((FrameworkElement)sender).DataContext is string name)
            await ShareUi.ShareInstanceAsync(_app, _inst, overlayProfile: name);
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || ((FrameworkElement)sender).DataContext is not string name)
            return;
        if (!await _app.Dialogs.ConfirmAsync("Profil löschen", $"Das Overlay-Profil \"{name}\" löschen?", "Löschen", danger: true))
            return;
        OverlayConfigFile.DeleteProfile(_inst, name);
        RefreshProfiles();
    }

    private static (int Mods, int Worlds, long Size) Measure(Installation inst)
    {
        try
        {
            var modsDir = Path.Combine(inst.GameDir, "mods");
            var mods = Directory.Exists(modsDir) ? Directory.GetFiles(modsDir, "*.jar").Length : 0;
            var saves = Path.Combine(inst.GameDir, "saves");
            var worlds = Directory.Exists(saves) ? Directory.GetDirectories(saves).Length : 0;
            long size = 0;
            if (Directory.Exists(inst.GameDir))
                foreach (var file in new DirectoryInfo(inst.GameDir).EnumerateFiles("*", SearchOption.AllDirectories))
                    size += file.Length;
            return (mods, worlds, size);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    private static string FormatDuration(long seconds)
    {
        if (seconds < 60)
            return "0 Min";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} Std {span.Minutes} Min" : $"{span.Minutes} Min";
    }

    private static string FormatAgo(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        if (age.TotalMinutes < 2)
            return "gerade eben";
        if (age.TotalHours < 1)
            return $"vor {(int)age.TotalMinutes} Min";
        if (age.TotalDays < 1)
            return $"vor {(int)age.TotalHours} Std";
        return age.TotalDays < 2 ? "gestern" : $"vor {(int)age.TotalDays} Tagen";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024} KB"
    };

    private void Ram_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null)
            return;
        _inst.MaxRamMb = RamAdvisor.Recommend(_inst).Mb;
        _app.NotifyInstallationsChanged();
        Refresh();
    }

    private async void Crash_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null)
            return;
        await CrashUi.ShowAsync(_app, _inst);
        Refresh();
    }
}
