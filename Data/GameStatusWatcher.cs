using System.Diagnostics;
using System.IO;

namespace McLauncher;

/// <summary>
/// Beobachtet ein laufendes Spiel: Die AxoClient-Mod schreibt den aktuellen Server nach "axoclient-status.txt".
/// Änderungen gehen an Discord (Beitreten-Knopf) und an den Dienst (Freundesliste); jede Minute kommt ein
/// Lebenszeichen, damit man bei den Freunden als "im Spiel" gilt.
/// </summary>
public sealed class GameStatusWatcher
{
    public const string StatusFileName = "axoclient-status.txt";

    private readonly AppState _app;
    private readonly Installation _inst;
    private readonly string _file;
    private readonly Timer _timer;
    private string? _server;
    private DateTime _lastReport = DateTime.MinValue;
    private int _ticking;

    private GameStatusWatcher(AppState app, Installation inst, QuickPlay? quickPlay)
    {
        _app = app;
        _inst = inst;
        _file = Path.Combine(inst.GameDir, StatusFileName);
        _server = quickPlay?.Server; // ohne Mod bleibt es bei der Adresse, mit der gestartet wurde
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    /// <summary>Vor dem Spielstart aufrufen: entfernt eine alte Statusdatei.</summary>
    public static void Reset(Installation inst)
    {
        try
        {
            File.Delete(Path.Combine(inst.GameDir, StatusFileName));
        }
        catch
        {
            // wird von der Mod ohnehin überschrieben
        }
    }

    /// <summary>Beginnt mit der Beobachtung und hört auf, sobald das Spiel beendet ist.</summary>
    public static void Watch(AppState app, Installation inst, QuickPlay? quickPlay, Process process)
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
                    _lastReport = DateTime.MinValue; // Freunden sofort melden
                }
            }
            if (DateTime.UtcNow - _lastReport > TimeSpan.FromSeconds(60))
            {
                _lastReport = DateTime.UtcNow;
                if (_app.Friends.Available)
                    _app.Friends.SetStatusAsync(true, _server, _inst.MinecraftVersion).Wait();
            }
        }
        catch
        {
            // Datei gerade in Arbeit oder Dienst nicht erreichbar: beim nächsten Mal
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private void Stop()
    {
        _timer.Dispose();
        if (_app.Friends.Available)
            _app.Friends.SetStatusAsync(false, null, null).ContinueWith(_ => { }, TaskScheduler.Default);
    }
}
