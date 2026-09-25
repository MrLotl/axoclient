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
    private DateTime _lastReport = DateTime.MinValue;
    private int _ticking;

    private GameStatusWatcher(AppServices app, Installation inst, QuickPlay? quickPlay)
    {
        _app = app;
        _inst = inst;
        _file = inst.GameFile(StatusFileName);
        _server = quickPlay?.Server;
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
            if (DateTime.UtcNow - _lastReport > HeartbeatInterval)
            {
                _lastReport = DateTime.UtcNow;
                if (_app.Axo.Available)
                    _app.Axo.SetStatusAsync(true, _server, _inst.MinecraftVersion).Wait();
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

    private void Stop()
    {
        _timer.Dispose();
        if (_app.Axo.Available)
            _app.Axo.SetStatusAsync(false, null, null).ContinueWith(_ => { }, TaskScheduler.Default);
    }
}
