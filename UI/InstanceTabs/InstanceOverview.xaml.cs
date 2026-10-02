using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AxoClient.UI.InstanceTabs;

public partial class InstanceOverview : UserControl
{
    private static readonly Color[] Levels =
    [
        Color.FromRgb(0x33, 0x33, 0x33), Color.FromRgb(0x4A, 0x26, 0x47), Color.FromRgb(0x7A, 0x2F, 0x73),
        Color.FromRgb(0xB0, 0x3D, 0x9E), Color.FromRgb(0xE8, 0x65, 0xC4)
    ];

    private static readonly string[] WeekdaysShort = ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"];
    private static readonly string[] WeekdaysLong = ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"];
    private const string HoverHint = "Fahre über einen Tag, um die Spielzeit zu sehen";

    private AppServices _app = null!;
    private Installation? _inst;
    private int _request;
    private int _monthOffset;

    public event Action<InstanceSection>? OpenRequested;

    public InstanceOverview()
    {
        InitializeComponent();
        foreach (var level in Levels)
            Legend.Children.Add(new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(3), Background = Ui.Frozen(level), Margin = new Thickness(0, 0, 3, 0) });
    }

    public void Show(AppServices app, Installation inst)
    {
        _app = app;
        if (_inst != inst)
            _monthOffset = 0;
        _inst = inst;
        Refresh();
    }

    public async void Refresh()
    {
        if (_inst is not { } inst)
            return;
        var request = ++_request;

        TotalText.Text = Formats.Hours(inst.PlayTimeSeconds);
        TotalSub.Text = PlayHistory.FirstDay(inst) is { } first
            ? $"seit {first.Day}. {Formats.MonthsLong[first.Month - 1]} {first.Year}"
            : "Noch nicht gespielt";

        var monday = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
        long week = 0, previous = 0;
        WeekBars.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            var day = monday.AddDays(i);
            var seconds = day <= DateTime.Today ? PlayHistory.Seconds(inst, day) : 0;
            week += seconds;
            previous += PlayHistory.Seconds(inst, day.AddDays(-7));
            var height = Math.Max(3, Math.Min(seconds / 60.0, 300) / 300 * 30);
            WeekBars.Children.Add(new Border
            {
                Width = 6,
                Height = height,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(3, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Ui.Frozen(day > DateTime.Today ? Color.FromRgb(0x30, 0x30, 0x30)
                    : seconds > 0 ? Color.FromRgb(0xD3, 0x5B, 0xB8) : Color.FromRgb(0x3A, 0x3A, 0x3A)),
                ToolTip = $"{WeekdaysShort[i]}: {Duration(seconds)}"
            });
        }
        WeekText.Text = Duration(week);
        var diff = week - previous;
        WeekSub.Text = (diff >= 0 ? "+" : "−") + Duration(Math.Abs(diff)) + " ggü. Vorwoche";

        if (inst.LastPlayedUtc is { } last)
        {
            var local = last.ToLocalTime();
            LastText.Text = $"{Formats.Day(last)}, {local:HH:mm}";
            LastSub.Text = inst.LastSessionSeconds > 0 ? "Sitzung: " + Duration(inst.LastSessionSeconds) : $"{Formats.Count(inst.LaunchCount, "Start", "Starts")} insgesamt";
        }
        else
        {
            LastText.Text = "Noch nie";
            LastSub.Text = "Starte die Instanz oben mit „Spielen“";
        }

        ShowActivity(inst);

        SizeText.Text = "…";
        var sizes = await Task.Run(() => (
            Worlds: FileOps.DirectorySize(inst.SavesDir),
            Mods: FileOps.DirectorySize(inst.ModsDir),
            Total: FileOps.DirectorySize(inst.GameDir)));
        if (request != _request)
            return;
        var rest = Math.Max(0, sizes.Total - sizes.Worlds - sizes.Mods);
        SizeText.Text = Formats.Size(sizes.Total).Replace('.', ',');
        WorldsCol.Width = new GridLength(Math.Max(sizes.Worlds, 1), GridUnitType.Star);
        ModsCol.Width = new GridLength(Math.Max(sizes.Mods, 1), GridUnitType.Star);
        RestCol.Width = new GridLength(Math.Max(rest, 1), GridUnitType.Star);
        SizeSub.Text = $"Welten {Formats.Size(sizes.Worlds)} · Mods {Formats.Size(sizes.Mods)} · Rest {Formats.Size(rest)}".Replace('.', ',');
    }

    private static string Duration(long seconds)
    {
        if (seconds < 60)
            return seconds > 0 ? "unter 1 Min" : "0 Min";
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        return hours > 0 ? minutes > 0 ? $"{hours} Std {minutes} Min" : $"{hours} Std" : $"{minutes} Min";
    }

    private static int Level(long seconds)
    {
        var minutes = seconds / 60;
        return seconds <= 0 ? 0 : minutes < 60 ? 1 : minutes < 120 ? 2 : minutes < 210 ? 3 : 4;
    }

    private static string DateLabel(DateTime date) =>
        $"{WeekdaysShort[((int)date.DayOfWeek + 6) % 7]}, {date.Day}. {Formats.Months[date.Month - 1]} {date.Year}";

    private void Range_Checked(object sender, RoutedEventArgs e)
    {
        if (_inst != null)
            ShowActivity(_inst);
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _monthOffset--;
        if (_inst != null)
            ShowActivity(_inst);
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_monthOffset >= 0)
            return;
        _monthOffset++;
        if (_inst != null)
            ShowActivity(_inst);
    }

    private void ShowActivity(Installation inst)
    {
        var year = YearToggle.IsChecked == true;
        Ui.Show(YearView, year);
        Ui.Show(MonthView, !year);
        Ui.Show(MonthNav, !year);
        HoverText.Text = HoverHint;

        var today = DateTime.Today;
        var lastMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var firstMonday = lastMonday.AddDays(-52 * 7);
        if (year)
            BuildYear(inst, firstMonday);
        else
            BuildMonth(inst);

        var from = year ? firstMonday : new DateTime(today.Year, today.Month, 1).AddMonths(_monthOffset);
        var to = year ? today : from.AddMonths(1).AddDays(-1);
        if (to > today)
            to = today;
        long total = 0;
        var playDays = 0;
        var byWeekday = new long[7];
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var seconds = PlayHistory.Seconds(inst, day);
            total += seconds;
            if (seconds <= 0)
                continue;
            playDays++;
            byWeekday[(int)day.DayOfWeek] += seconds;
        }
        ActivitySub.Text = year ? Duration(total) + " im letzten Jahr" : Duration(total) + " in diesem Monat";
        DaysValue.Text = Formats.Count(playDays, "Tag", "Tage");
        AvgValue.Text = playDays > 0 ? Duration(total / playDays) : "–";
        StreakValue.Text = Formats.Count(PlayHistory.LongestStreak(inst, from, to), "Tag", "Tage");
        WeekdayValue.Text = byWeekday.Max() > 0 ? WeekdaysLong[Array.IndexOf(byWeekday, byWeekday.Max())] : "–";
    }

    private Border Cell(DateTime date, long seconds, bool future, double width, double height)
    {
        var cell = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(3),
            Background = future ? Brushes.Transparent : Ui.Frozen(Levels[Level(seconds)]),
            Tag = $"{DateLabel(date)}: {(seconds > 0 ? Duration(seconds) : "Nicht gespielt")}"
        };
        if (!future)
        {
            cell.ToolTip = cell.Tag;
            Brush? savedBrush = null;
            var savedThickness = new Thickness(0);
            cell.MouseEnter += (_, _) =>
            {
                savedBrush = cell.BorderBrush;
                savedThickness = cell.BorderThickness;
                cell.BorderBrush = Ui.Resource<Brush>("TextStrong");
                cell.BorderThickness = new Thickness(1.5);
                HoverText.Text = (string)cell.Tag;
            };
            cell.MouseLeave += (_, _) =>
            {
                cell.BorderBrush = savedBrush;
                cell.BorderThickness = savedThickness;
            };
        }
        return cell;
    }

    private void BuildYear(Installation inst, DateTime firstMonday)
    {
        YearView.Children.Clear();
        var labels = new StackPanel { Width = 22, Margin = new Thickness(0, 19, 6, 0) };
        for (var i = 0; i < 7; i++)
            labels.Children.Add(new TextBlock { Text = i is 0 or 2 or 4 ? WeekdaysShort[i] : "", FontSize = 10, Height = 16, Foreground = Ui.Resource<Brush>("DimText") });
        YearView.Children.Add(labels);

        var lastMonth = -1;
        for (var week = 0; week < 53; week++)
        {
            var column = new StackPanel { Margin = new Thickness(0, 0, 3, 0) };
            var weekStart = firstMonday.AddDays(week * 7);
            var label = "";
            if (weekStart.Month != lastMonth)
            {
                label = week == 0 ? "" : Formats.Months[weekStart.Month - 1];
                lastMonth = weekStart.Month;
            }
            column.Children.Add(new Canvas
            {
                Height = 13,
                Width = 13,
                Margin = new Thickness(0, 0, 0, 6),
                Children = { new TextBlock { Text = label, FontSize = 10, Foreground = Ui.Resource<Brush>("DimText") } }
            });
            for (var day = 0; day < 7; day++)
            {
                var date = weekStart.AddDays(day);
                var cell = Cell(date, PlayHistory.Seconds(inst, date), date > DateTime.Today, 13, 13);
                cell.Margin = new Thickness(0, 0, 0, 3);
                column.Children.Add(cell);
            }
            YearView.Children.Add(column);
        }
    }

    private void BuildMonth(Installation inst)
    {
        MonthView.Children.Clear();
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(_monthOffset);
        MonthTitle.Text = $"{Formats.MonthsLong[first.Month - 1]} {first.Year}";
        NextMonthButton.IsEnabled = _monthOffset < 0;

        var header = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, -6, 6) };
        foreach (var name in WeekdaysShort)
            header.Children.Add(new TextBlock { Text = name, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("DimText"), Margin = new Thickness(9, 0, 6, 0) });
        MonthView.Children.Add(header);

        var grid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, -6, 0) };
        var lead = ((int)first.DayOfWeek + 6) % 7;
        for (var i = 0; i < lead; i++)
            grid.Children.Add(new Border { Height = 32, Margin = new Thickness(0, 0, 6, 6) });
        var days = DateTime.DaysInMonth(first.Year, first.Month);
        for (var d = 1; d <= days; d++)
        {
            var date = new DateTime(first.Year, first.Month, d);
            var future = date > DateTime.Today;
            var seconds = future ? 0 : PlayHistory.Seconds(inst, date);
            var level = Level(seconds);
            var cell = Cell(date, seconds, future, double.NaN, 32);
            cell.Margin = new Thickness(0, 0, 6, 6);
            cell.CornerRadius = new CornerRadius(7);
            cell.Padding = new Thickness(9, 0, 9, 0);
            if (future)
            {
                cell.BorderBrush = Ui.Frozen(Color.FromRgb(0x2E, 0x2E, 0x2E));
                cell.BorderThickness = new Thickness(1);
            }
            else if (date == DateTime.Today)
            {
                cell.BorderBrush = Ui.Resource<Brush>("AccentText");
                cell.BorderThickness = new Thickness(1.5);
            }
            var minutes = seconds / 60;
            var value = seconds <= 0 ? "" : minutes >= 60
                ? (minutes / 60.0).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " h"
                : minutes + " m";
            var dock = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            var valueText = new TextBlock { Text = value, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Frozen(level >= 3 ? Colors.White : Color.FromRgb(0xF2, 0xD9, 0xF6)) };
            DockPanel.SetDock(valueText, Dock.Right);
            dock.Children.Add(valueText);
            dock.Children.Add(new TextBlock
            {
                Text = d.ToString(),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ui.Frozen(future ? Color.FromRgb(0x4F, 0x4F, 0x4F) : level >= 3 ? Colors.White : level > 0 ? Color.FromRgb(0xE0, 0xC6, 0xE6) : Color.FromRgb(0x8A, 0x8A, 0x8A))
            });
            cell.Child = dock;
            grid.Children.Add(cell);
        }
        MonthView.Children.Add(grid);
    }
}
