using System.Globalization;

namespace AxoClient.Instances;

public record PlayDay(DateTime Date, long Seconds)
{
    public string ShortWeekday => Date.ToString("ddd", CultureInfo.CurrentCulture)[..2];

    public bool IsToday => Date.Date == DateTime.Today;
}

public static class PlayHistory
{
    private const int KeepDays = 400;
    private const string KeyFormat = "yyyy-MM-dd";

    public static void Add(Installation inst, DateTime localStart, long seconds)
    {
        if (seconds <= 0)
            return;
        inst.PlayDays ??= [];
        var key = Key(localStart);
        inst.PlayDays[key] = inst.PlayDays.GetValueOrDefault(key) + seconds;

        if (inst.PlayDays.Count <= KeepDays)
            return;
        var cutoff = DateTime.Today.AddDays(-KeepDays);
        foreach (var old in inst.PlayDays.Keys.Where(k => Parse(k) is { } date && date < cutoff).ToList())
            inst.PlayDays.Remove(old);
    }

    public static List<PlayDay> Recent(Installation inst, int days)
    {
        var history = inst.PlayDays ?? [];
        var start = DateTime.Today.AddDays(-(days - 1));
        return Enumerable.Range(0, days)
            .Select(offset => start.AddDays(offset))
            .Select(date => new PlayDay(date, history.GetValueOrDefault(Key(date))))
            .ToList();
    }

    public static long LastWeekSeconds(Installation inst) => Recent(inst, 7).Sum(d => d.Seconds);

    public static PlayDay? BestDay(Installation inst)
    {
        var best = (inst.PlayDays ?? []).Where(e => e.Value > 0)
            .OrderByDescending(e => e.Value).ThenByDescending(e => e.Key).FirstOrDefault();
        return best.Key == null || Parse(best.Key) is not { } date ? null : new PlayDay(date, best.Value);
    }

    public static int CurrentStreak(Installation inst)
    {
        var history = inst.PlayDays ?? [];
        if (history.Count == 0)
            return 0;
        var day = history.GetValueOrDefault(Key(DateTime.Today)) > 0 ? DateTime.Today : DateTime.Today.AddDays(-1);
        var streak = 0;
        while (history.GetValueOrDefault(Key(day)) > 0)
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }

    private static string Key(DateTime date) => date.Date.ToString(KeyFormat);

    private static DateTime? Parse(string key) =>
        DateTime.TryParseExact(key, KeyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
