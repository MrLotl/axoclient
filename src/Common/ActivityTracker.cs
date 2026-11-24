using System.IO.Abstractions;

namespace AxoClient.Common
{
    /// <summary>Spielzeit bzw. Laufzeit pro Tag mitschreiben. Eine Sitzung über Mitternacht wird aufgeteilt.</summary>
    public sealed class ActivityTracker : IActivityTracker
    {
        private readonly IFileSystem _fs;
        private readonly IJsonStoreFactory _jsonStoreFactory;

        public ActivityTracker(IFileSystem fileSystem, IJsonStoreFactory jsonStoreFactory)
        {
            _fs = fileSystem;
            _jsonStoreFactory = jsonStoreFactory;
        }

        public async Task<ActivityLog> Load(string path) => await _jsonStoreFactory.Create<ActivityLog>(_fs, path).Load();

        public Task<ActivityLog> AddSession(string path, ActivitySessionKind kind, DateTimeOffset start, DateTimeOffset end) =>
            _jsonStoreFactory.Create<ActivityLog>(_fs, path).Update(log => AddSession(log, kind, start, end));

        internal void AddSession(ActivityLog log, ActivitySessionKind kind, DateTimeOffset start, DateTimeOffset end)
        {
            if (end <= start)
            {
                return;
            }

            var seconds = (long)(end - start).TotalSeconds;
            log.TotalSeconds += seconds;
            log.LastSessionSeconds = seconds;
            var firstDay = start.ToLocalTime().ToString("yyyy-MM-dd");
            if (kind == ActivitySessionKind.Playtime)
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
            foreach (var old in log.Days.Keys.Where(key => string.CompareOrdinal(key, DateTime.Now.AddDays(-400).ToString("yyyy-MM-dd")) < 0).ToList())
            {
                log.Days.Remove(old);
            }
        }
    }
}
