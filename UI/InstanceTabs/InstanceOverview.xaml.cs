using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.InstanceTabs;

public partial class InstanceOverview : UserControl
{
    private const int ChartDays = 14;
    private const double ChartHeight = 66;

    private sealed record Stat(string Label, string Value);

    private sealed record Bar(string Label, double BarHeight, string Tip, Brush Fill, Brush LabelBrush);

    private AppServices _app = null!;
    private Installation? _inst;
    private int _request;

    public InstanceOverview()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation inst)
    {
        _app = app;
        _inst = inst;
        Refresh();
    }

    public async void Refresh()
    {
        if (_inst is not { } inst)
            return;
        var request = ++_request;

        var stats = new List<Stat>
        {
            new("Spielzeit", Formats.Duration(inst.PlayTimeSeconds)),
            new("Diese Woche", Formats.Duration(PlayHistory.LastWeekSeconds(inst))),
            new("Starts", inst.LaunchCount.ToString()),
            new("Zuletzt gespielt", inst.LastPlayedUtc is { } last ? Formats.Ago(last) : "nie"),
            new("Abstürze", inst.CrashCount.ToString())
        };
        StatsList.ItemsSource = stats.ToList();
        ShowPlayChart(inst);
        ShowRamAdvice(inst);

        CrashText.Text = inst.LastCrashUtc is { } crash
            ? $"Letzter Absturz {Formats.Ago(crash)}. Die Analyse sucht die Ursache und kann sie direkt beheben."
            : "Kein Absturz aufgezeichnet. Die Analyse liest trotzdem das letzte Log, falls etwas nicht stimmt.";

        var (mods, worlds, size) = await Task.Run(() => (
            FileOps.CountEntries(inst.ModsDir, "*.jar"),
            WorldStore.Count(inst.SavesDir),
            FileOps.DirectorySize(inst.GameDir)));
        if (request != _request)
            return;
        stats.Add(new Stat("Mods", inst.CanUseMods ? mods.ToString() : "-"));
        stats.Add(new Stat("Welten", worlds.ToString()));
        stats.Add(new Stat("Ordnergröße", Formats.Size(size)));
        stats.Add(new Stat("Loader", inst.Loader.ToString()));
        StatsList.ItemsSource = stats;
    }

    private void ShowRamAdvice(Installation inst)
    {
        var advice = RamAdvisor.Recommend(inst);
        var current = RamAdvisor.CurrentMb(inst, _app.Settings);
        var source = inst.MaxRamMb == null ? "aus den Launcher-Einstellungen" : "nur für diese Instanz";
        if (current == advice.Mb)
        {
            RamText.Text = $"{Formats.Megabytes(current)} ({source}) passen zu dieser Instanz ({advice.Reason}).";
            RamButton.Visibility = Visibility.Collapsed;
            return;
        }
        RamText.Text = $"Aktuell {Formats.Megabytes(current)} ({source}). Empfohlen: {Formats.Megabytes(advice.Mb)} " +
                       $"({advice.Reason}).";
        RamButton.Visibility = Visibility.Visible;
        RamButton.IsEnabled = !_app.Games.IsRunning(inst);
    }

    private void ShowPlayChart(Installation inst)
    {
        var days = PlayHistory.Recent(inst, ChartDays);
        var max = Math.Max(days.Max(d => d.Seconds), 1);
        var accent = Ui.Resource<Brush>("Accent");
        var muted = Ui.Resource<Brush>("MutedText");

        PlayChart.ItemsSource = days.Select(day => new Bar(
            day.ShortWeekday,
            day.Seconds == 0 ? 0 : Math.Max(3, day.Seconds / (double)max * ChartHeight),
            $"{day.Date:dddd, dd.MM.yyyy}: {Formats.Duration(day.Seconds)}",
            accent,
            day.IsToday ? Brushes.White : muted)).ToList();

        var streak = PlayHistory.CurrentStreak(inst);
        var best = PlayHistory.BestDay(inst);
        PlaySummary.Text = inst.PlayTimeSeconds == 0
            ? "Noch nicht gespielt. Sobald du startest, siehst du hier die letzten zwei Wochen."
            : $"Letzte 7 Tage: {Formats.Duration(PlayHistory.LastWeekSeconds(inst))}." +
              (streak > 1 ? $" {streak} Tage in Folge gespielt." : "") +
              (best != null ? $" Bester Tag: {best.Date:dd.MM.yyyy} mit {Formats.Duration(best.Seconds)}." : "");
    }

    private void Ram_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null)
            return;
        _inst.MaxRamMb = RamAdvisor.Recommend(_inst).Mb;
        _app.Instances.NotifyChanged();
        Refresh();
    }

    private async void Crash_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null)
            return;
        await CrashDialog.ShowAsync(_app, _inst);
        Refresh();
    }
}
