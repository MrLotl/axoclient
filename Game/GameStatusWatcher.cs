using System.Diagnostics;

namespace AxoClient.Game;

public sealed class GameStatusWatcher
{
    private const string StatusFileName = "axoclient-status.txt";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);

    private readonly AppServices _app;
    private readonly Installation _inst;
    private readonly string _file;
    private readonly Timer _timer;
    private string? _server;
    private string? _relay;
    private string? _joinable;
    private long _logOffset;
    private DateTime _lastReport = DateTime.MinValue;
    private int _ticking;

    private GameStatusWatcher(AppServices app, Installation inst, QuickPlay? quickPlay)
    {
        _app = app;
        _inst = inst;
        _file = inst.GameFile(StatusFileName);
        _server = quickPlay?.Server;
        _logOffset = JoinRelay.IsActiveFor(inst, app.Settings) && File.Exists(inst.LatestLog) ? new FileInfo(inst.LatestLog).Length : -1;
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, PollInterval);
    }

    public static void Reset(Installation inst) => FileOps.TryDelete(inst.GameFile(StatusFileName));

    public static void Watch(AppServices app, Installation inst, QuickPlay? quickPlay, Process process)
    {
        var watcher = new GameStatusWatcher(app, inst, quickPlay);
        process.Exited += (_, _) => watcher.Stop();
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
            return;
        try
        {
            if (File.Exists(_file))
            {
                var text = File.ReadAllText(_file).Trim();
                var server = text.Length > 0 ? text : null;
                if (server != _server)
                {
                    _server = server;
                    _app.Discord.ServerChanged(server);
                    _lastReport = DateTime.MinValue;
                }
            }
            if (_logOffset >= 0 && ReadRelayDomain() is var relay && relay != _relay)
            {
                _relay = relay;
                _lastReport = DateTime.MinValue;
            }
            var joinable = _server ?? _relay;
            if (joinable != _joinable)
            {
                _joinable = joinable;
                _app.Games.CurrentServer = joinable == null ? null : (joinable, _inst.MinecraftVersion);
            }
            if (DateTime.UtcNow - _lastReport > HeartbeatInterval)
            {
                _lastReport = DateTime.UtcNow;
                if (_app.Axo.Available)
                    _app.Axo.SetStatusAsync(true, _app.LocalServers.FriendAddressFor(joinable), _inst.MinecraftVersion, _inst.Name).Wait();
            }
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Spielstatus an den Dienst melden", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private string? ReadRelayDomain()
    {
        var log = _inst.LatestLog;
        if (!File.Exists(log))
            return _relay;
        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < _logOffset)
            _logOffset = 0;
        if (stream.Length == _logOffset)
            return _relay;
        stream.Seek(_logOffset, SeekOrigin.Begin);
        var bytes = new byte[stream.Length - _logOffset];
        stream.ReadExactly(bytes);
        var complete = Array.LastIndexOf(bytes, (byte)'\n') + 1;
        _logOffset += complete;
        var relay = _relay;
        foreach (var line in System.Text.Encoding.UTF8.GetString(bytes, 0, complete).Split('\n'))
        {
            if (JoinRelay.DomainFrom(line) is { } domain)
                relay = domain;
            else if (JoinRelay.IsWorldClosed(line))
                relay = null;
        }
        return relay;
    }

    private void Stop()
    {
        _timer.Dispose();
        _app.Games.CurrentServer = null;
        if (_app.Axo.Available)
            _app.Axo.SetStatusAsync(false, null, null).ContinueWith(_ => { }, TaskScheduler.Default);
    }
}
