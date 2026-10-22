using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AxoClient.Core.Common;
using AxoClient.Core.Launch;

namespace AxoClient.Core.Instances;

/// <summary>Spielzeit an einem Ort: Einzelspieler-Welt (Ordnername) oder Server (Adresse).</summary>
public sealed class PlaceEntry
{
    /// <summary><c>world</c> oder <c>server</c>.</summary>
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public long Seconds { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }
}

/// <summary><c>instances/&lt;id&gt;/places.json</c></summary>
public sealed class PlacesLog : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public List<PlaceEntry> Places { get; set; } = [];
}

/// <summary>Für die Übersicht: Ort mit Anzeigename und Bild.</summary>
public sealed record PlaceView(string Kind, string Id, string Name, string? IconUrl, long Seconds, DateTimeOffset? LastPlayed, bool Exists);

/// <summary>
/// Schreibt aus dem Spiel-Log mit, wie lange auf welchem Server und in welcher Einzelspieler-Welt gespielt wurde –
/// unabhängig von der gespeicherten Serverliste. Server: von "Connecting to …" bis zum Trennen. Welt: vom Start des
/// integrierten Servers bis zu seinem Ende; welche Welt es war, verrät die beim Laden geschriebene <c>session.lock</c>.
/// </summary>
public sealed partial class PlaceTracker(AppPaths paths) : IGameLaunchHook
{
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

    void IGameLaunchHook.OnStarted(LaunchContext ctx, RunningProcess process)
    {
        var session = new Session { Context = ctx };
        if (ctx.QuickPlay?.Server is { Length: > 0 } server)
            Open(session, "server", server);
        session.Handler = line => OnLine(session, line);
        process.LineReceived += session.Handler;
        _sessions[process] = session;
    }

    void IGameLaunchHook.OnExited(LaunchContext ctx, RunningProcess process, int exitCode, bool crashed)
    {
        if (!_sessions.TryRemove(process, out var session))
            return;
        if (session.Handler != null)
            process.LineReceived -= session.Handler;
        Close(session);
    }

    private void OnLine(Session session, LogLine line)
    {
        var text = line.Text;
        if (Connecting().Match(text) is { Success: true } connect)
        {
            var port = int.TryParse(connect.Groups[2].Value, out var p) ? p : 25565;
            Close(session);
            Open(session, "server", port == 25565 ? connect.Groups[1].Value : $"{connect.Groups[1].Value}:{port}");
        }
        else if (text.Contains("Starting integrated minecraft server", StringComparison.Ordinal))
        {
            Close(session);
            // Welt erst beim Ende bestimmen, dann liegt die session.lock sicher vor
            Open(session, "world", null);
        }
        else if (text.Contains("Stopping singleplayer server", StringComparison.Ordinal)
                 || session.Kind == "server" && (text.Contains("Disconnecting from server", StringComparison.Ordinal)
                                                 || text.Contains("[CHAT] Disconnected", StringComparison.Ordinal)))
            Close(session);
    }

    private static void Open(Session session, string kind, string? id)
    {
        session.Kind = kind;
        session.Id = id;
        session.Since = DateTimeOffset.Now;
    }

    private void Close(Session session)
    {
        if (session.Kind == null)
            return;
        var kind = session.Kind;
        var id = session.Id ?? (kind == "world" ? FindWorld(session.Context.GameDir, session.Since) : null);
        var start = session.Since;
        var end = DateTimeOffset.Now;
        session.Kind = null;
        session.Id = null;
        if (id == null || end - start < TimeSpan.FromSeconds(5))
            return;
        _ = SaveAsync(session.Context.Instance.Id, kind, id, (long)(end - start).TotalSeconds, end);
    }

    /// <summary>Die Welt, deren session.lock seit Beginn der Sitzung geschrieben wurde (die neueste).</summary>
    private static string? FindWorld(string gameDir, DateTimeOffset since)
    {
        try
        {
            var saves = Path.Combine(gameDir, "saves");
            if (!Directory.Exists(saves))
                return null;
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

    private async Task SaveAsync(string instanceId, string kind, string id, long seconds, DateTimeOffset end)
    {
        await _write.WaitAsync();
        try
        {
            await new JsonStore<PlacesLog>(paths.Fs, paths.PlacesJson(instanceId)).UpdateAsync(log =>
            {
                var entry = log.Places.FirstOrDefault(p => p.Kind == kind && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    log.Places.Add(entry = new PlaceEntry { Kind = kind, Id = id });
                entry.Seconds += seconds;
                entry.LastPlayed = end;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AxoException)
        {
            AppLog.Warn("Spielzeit pro Ort konnte nicht gespeichert werden", ex);
        }
        finally
        {
            _write.Release();
        }
    }

    public Task<PlacesLog> LoadAsync(string instanceId, CancellationToken ct = default) =>
        new JsonStore<PlacesLog>(paths.Fs, paths.PlacesJson(Text.RequireId(instanceId))).LoadAsync(ct);

    [GeneratedRegex(@"Connecting to ([A-Za-z0-9.\-]+), (\d{1,5})")]
    private static partial Regex Connecting();
}
