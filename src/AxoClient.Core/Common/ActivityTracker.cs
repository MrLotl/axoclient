using System.IO.Abstractions;

namespace AxoClient.Core.Common;

/// <summary>Inhalt von <c>playtime.json</c> (Instanz) bzw. <c>uptime.json</c> (lokaler Server).</summary>
public sealed class ActivityLog : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public long TotalSeconds { get; set; }

    /// <summary>Für Instanzen <c>firstPlayed</c>, für Server <c>firstStarted</c> (siehe JSON-Plan).</summary>
    public string? FirstPlayed { get; set; }
    public string? FirstStarted { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }
    public DateTimeOffset? LastStarted { get; set; }
    public long LastSessionSeconds { get; set; }
    public SortedDictionary<string, long> Days { get; set; } = new();
}

/// <summary>Spielzeit bzw. Laufzeit pro Tag mitschreiben. Eine Sitzung über Mitternacht wird aufgeteilt.</summary>
public sealed class ActivityTracker(IFileSystem fs)
{
    public enum Kind
    {
        Playtime,
        Uptime
    }

    public async Task<ActivityLog> LoadAsync(string path) => await new JsonStore<ActivityLog>(fs, path).LoadAsync();

    public Task<ActivityLog> AddSessionAsync(string path, Kind kind, DateTimeOffset start, DateTimeOffset end) =>
        new JsonStore<ActivityLog>(fs, path).UpdateAsync(log => AddSession(log, kind, start, end));

    internal static void AddSession(ActivityLog log, Kind kind, DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
            return;
        var seconds = (long)(end - start).TotalSeconds;
        log.TotalSeconds += seconds;
        log.LastSessionSeconds = seconds;
        var firstDay = start.ToLocalTime().ToString("yyyy-MM-dd");
        if (kind == Kind.Playtime)
        {
            log.FirstPlayed ??= firstDay;
            log.LastPlayed = end.ToLocalTime();
        }
        else
        {
            log.FirstStarted ??= firstDay;
            log.LastStarted = start.ToLocalTime();
        }

        var cursor = start.ToLocalTime();
        var stop = end.ToLocalTime();
        while (cursor < stop)
        {
            var nextMidnight = new DateTimeOffset(cursor.Date.AddDays(1), cursor.Offset);
            var segmentEnd = nextMidnight < stop ? nextMidnight : stop;
            var key = cursor.ToString("yyyy-MM-dd");
            log.Days[key] = log.Days.GetValueOrDefault(key) + (long)(segmentEnd - cursor).TotalSeconds;
            cursor = segmentEnd;
        }

        // Nur ein Jahr Tageswerte behalten
        foreach (var old in log.Days.Keys.Where(k => string.CompareOrdinal(k, DateTime.Now.AddDays(-400).ToString("yyyy-MM-dd")) < 0).ToList())
            log.Days.Remove(old);
    }
}
