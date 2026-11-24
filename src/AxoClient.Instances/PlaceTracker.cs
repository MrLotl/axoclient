using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AxoClient.Common;
using AxoClient.DataModel.Instances;
using AxoClient.DataModel.Launch;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;

namespace AxoClient.Instances
{
    /// <summary>
    /// Schreibt aus dem Spiel-Log mit, wie lange auf welchem Server und in welcher Einzelspieler-Welt gespielt wurde –
    /// unabhängig von der gespeicherten Serverliste. Server: von "Connecting to …" bis zum Trennen. Welt: vom Start des
    /// integrierten Servers bis zu seinem Ende; welche Welt es war, verrät die beim Laden geschriebene <c>session.lock</c>.
    /// </summary>
    public sealed partial class PlaceTracker : IGameLaunchHook, IPlaceTracker
    {
        private readonly AppPaths _paths;
        private readonly ITextHelper _textHelper;
        private readonly IAppLog _appLog;
        private readonly IJsonStoreFactory _jsonStoreFactory;

        private sealed class Session
        {
            public required LaunchContext Context { get; init; }
            public Action<LogLine>? Handler { get; set; }
            public string? Kind { get; set; }
            public string? Id { get; set; }
            public DateTimeOffset Since { get; set; }
        }

        private readonly ConcurrentDictionary<RunningProcess, Session> _sessions = new();
        private readonly SemaphoreSlim _write = new(1, 1);

        public PlaceTracker(AppPaths paths, ITextHelper textHelper, IAppLog appLog, IJsonStoreFactory jsonStoreFactory)
        {
            _paths = paths;
            _textHelper = textHelper;
            _appLog = appLog;
            _jsonStoreFactory = jsonStoreFactory;
        }

        void IGameLaunchHook.OnStarted(LaunchContext ctx, RunningProcess process)
        {
            var session = new Session { Context = ctx };
            if (ctx.QuickPlay?.Server is { Length: > 0 } server)
            {
                open(session, "server", server);
            }

            session.Handler = line => onLine(session, line);
            process.LineReceived += session.Handler;
            _sessions[process] = session;
        }

        void IGameLaunchHook.OnExited(LaunchContext ctx, RunningProcess process, int exitCode, bool crashed)
        {
            if (!_sessions.TryRemove(process, out var session))
            {
                return;
            }

            if (session.Handler != null)
            {
                process.LineReceived -= session.Handler;
            }

            close(session);
        }

        private void onLine(Session session, LogLine line)
        {
            var text = line.Text;
            if (connecting().Match(text) is { Success: true } connect)
            {
                var port = int.TryParse(connect.Groups[2].Value, out var p) ? p : 25565;
                close(session);
                open(session, "server", port == 25565 ? connect.Groups[1].Value : $"{connect.Groups[1].Value}:{port}");
            }
            else if (text.Contains("Starting integrated minecraft server", StringComparison.Ordinal))
            {
                close(session);
                // Welt erst beim Ende bestimmen, dann liegt die session.lock sicher vor
                open(session, "world", null);
            }
            else if (text.Contains("Stopping singleplayer server", StringComparison.Ordinal)
                     || session.Kind == "server" && (text.Contains("Disconnecting from server", StringComparison.Ordinal)
                                                     || text.Contains("[CHAT] Disconnected", StringComparison.Ordinal)))
            {
                close(session);
            }
        }

        private void open(Session session, string kind, string? id)
        {
            session.Kind = kind;
            session.Id = id;
            session.Since = DateTimeOffset.Now;
        }

        private void close(Session session)
        {
            if (session.Kind == null)
            {
                return;
            }

            var kind = session.Kind;
            var id = session.Id ?? (kind == "world" ? findWorld(session.Context.GameDir, session.Since) : null);
            var start = session.Since;
            var end = DateTimeOffset.Now;
            session.Kind = null;
            session.Id = null;
            if (id == null || end - start < TimeSpan.FromSeconds(5))
            {
                return;
            }

            _ = save(session.Context.Instance.Id, kind, id, (long)(end - start).TotalSeconds, end);
        }

        /// <summary>Die Welt, deren session.lock seit Beginn der Sitzung geschrieben wurde (die neueste).</summary>
        private string? findWorld(string gameDir, DateTimeOffset since)
        {
            try
            {
                var saves = Path.Combine(gameDir, "saves");
                if (!Directory.Exists(saves))
                {
                    return null;
                }

                return new DirectoryInfo(saves).EnumerateDirectories()
                    .Select(d => (Dir: d, Lock: new FileInfo(Path.Combine(d.FullName, "session.lock"))))
                    .Where(x => x.Lock.Exists && x.Lock.LastWriteTimeUtc >= since.UtcDateTime.AddSeconds(-10))
                    .OrderByDescending(x => x.Lock.LastWriteTimeUtc)
                    .Select(x => x.Dir.Name)
                    .FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private async Task save(string instanceId, string kind, string id, long seconds, DateTimeOffset end)
        {
            await _write.WaitAsync();
            try
            {
                await _jsonStoreFactory.Create<PlacesLog>(_paths.Fs, _paths.PlacesJson(instanceId)).Update(log =>
                {
                    var entry = log.Places.FirstOrDefault(p => p.Kind == kind && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                    {
                        log.Places.Add(entry = new PlaceEntry { Kind = kind, Id = id });
                    }

                    entry.Seconds += seconds;
                    entry.LastPlayed = end;
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AxoException)
            {
                _appLog.Warn("Spielzeit pro Ort konnte nicht gespeichert werden", ex);
            }
            finally
            {
                _write.Release();
            }
        }

        public Task<PlacesLog> Load(string instanceId, CancellationToken ct = default) =>
            _jsonStoreFactory.Create<PlacesLog>(_paths.Fs, _paths.PlacesJson(_textHelper.RequireId(instanceId))).Load(ct);

        [GeneratedRegex(@"Connecting to ([A-Za-z0-9.\-]+), (\d{1,5})")]
        private partial Regex connecting();
    }
}
