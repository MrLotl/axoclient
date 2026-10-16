using System.Diagnostics;

namespace AxoClient.Core.Common;

public enum LogLevel
{
    Info,
    Warn,
    Error,
    Debug
}

public sealed record LogLine(long Seq, DateTimeOffset Time, LogLevel Level, string Thread, string Text, bool StdErr);

/// <summary>
/// Ein laufender Java-Prozess (Instanz oder lokaler Server). Sammelt Log-Zeilen in einem Ringpuffer und
/// liefert sie live an die Konsole.
/// </summary>
public sealed class RunningProcess : IAsyncDisposable
{
    private const int MaxLines = 20_000;

    private readonly Process _process;
    private readonly LinkedList<LogLine> _lines = new();
    private readonly Lock _lock = new();
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _seq;

    internal RunningProcess(string key, Process process)
    {
        Key = key;
        _process = process;
        StartedAt = DateTimeOffset.Now;
    }

    public string Key { get; }
    public int Pid => _process.Id;
    public DateTimeOffset StartedAt { get; }
    public bool StopRequested { get; private set; }
    public bool HasExited => _exit.Task.IsCompleted;
    public Task<int> Exited => _exit.Task;

    public event Action<LogLine>? LineReceived;

    public IReadOnlyList<LogLine> Lines(int max = MaxLines)
    {
        lock (_lock)
            return _lines.Skip(Math.Max(0, _lines.Count - max)).ToList();
    }

    /// <summary>Für die Absturzanalyse: die letzten Zeilen als Text.</summary>
    public string Tail(int count = 400)
    {
        lock (_lock)
            return string.Join('\n', _lines.Skip(Math.Max(0, _lines.Count - count)).Select(l => l.Text));
    }

    internal void Start()
    {
        _process.OutputDataReceived += (_, e) => Add(e.Data, false);
        _process.ErrorDataReceived += (_, e) => Add(e.Data, true);
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            int code;
            try
            {
                code = _process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                code = -1;
            }
            _exit.TrySetResult(code);
        };
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task WriteLineAsync(string line)
    {
        if (HasExited)
            return;
        await _process.StandardInput.WriteLineAsync(line);
        await _process.StandardInput.FlushAsync();
    }

    /// <summary>Beendet den Prozess. Für Server zuerst sanft per Befehl, dann hart.</summary>
    public async Task StopAsync(string? gracefulCommand = null, TimeSpan? grace = null)
    {
        if (HasExited)
            return;
        StopRequested = true;
        if (gracefulCommand != null)
        {
            try
            {
                await WriteLineAsync(gracefulCommand);
                await Exited.WaitAsync(grace ?? TimeSpan.FromSeconds(30));
                return;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
            {
            }
        }
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Add(string? text, bool stdErr)
    {
        if (text == null)
            return;
        text = LogRedactor.Redact(text);
        var line = new LogLine(Interlocked.Increment(ref _seq), DateTimeOffset.Now, DetectLevel(text, stdErr),
            DetectThread(text), text, stdErr);
        lock (_lock)
        {
            _lines.AddLast(line);
            if (_lines.Count > MaxLines)
                _lines.RemoveFirst();
        }
        LineReceived?.Invoke(line);
    }

    internal static LogLevel DetectLevel(string text, bool stdErr)
    {
        // Typisches Minecraft-Format: [12:00:00] [Render thread/WARN]: ...
        if (text.Contains("/ERROR]") || text.Contains("/FATAL]") || text.Contains(" ERROR ") || text.StartsWith("Exception")
            || text.StartsWith("\tat ") || text.StartsWith("Caused by:"))
            return LogLevel.Error;
        if (text.Contains("/WARN]") || text.Contains(" WARN "))
            return LogLevel.Warn;
        if (text.Contains("/DEBUG]"))
            return LogLevel.Debug;
        return stdErr ? LogLevel.Warn : LogLevel.Info;
    }

    private static string DetectThread(string text)
    {
        var open = text.IndexOf("] [", StringComparison.Ordinal);
        if (open < 0)
            return "";
        var slash = text.IndexOf('/', open + 3);
        return slash > open ? text[(open + 3)..slash] : "";
    }

    public ValueTask DisposeAsync()
    {
        _process.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Startet und verwaltet Java-Prozesse von Instanzen und lokalen Servern.</summary>
public sealed class ProcessRunner
{
    private readonly Dictionary<string, RunningProcess> _running = new();
    private readonly Lock _lock = new();

    public event Action<RunningProcess>? Started;
    public event Action<RunningProcess, int>? Exited;

    public RunningProcess Start(string key, ProcessStartInfo info)
    {
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.RedirectStandardInput = true;
        info.CreateNoWindow = true;
        info.StandardOutputEncoding = System.Text.Encoding.UTF8;
        info.StandardErrorEncoding = System.Text.Encoding.UTF8;

        var running = new RunningProcess(key, new Process { StartInfo = info });
        running.Start();
        lock (_lock)
            _running[key] = running;
        Started?.Invoke(running);
        _ = WatchAsync(running);
        return running;
    }

    public RunningProcess? Get(string key)
    {
        lock (_lock)
            return _running.TryGetValue(key, out var p) && !p.HasExited ? p : null;
    }

    /// <summary>Auch bereits beendete Prozesse, damit die Konsole das letzte Log noch zeigt.</summary>
    public RunningProcess? GetIncludingExited(string key)
    {
        lock (_lock)
            return _running.GetValueOrDefault(key);
    }

    public IReadOnlyList<RunningProcess> All
    {
        get
        {
            lock (_lock)
                return _running.Values.ToList();
        }
    }

    public bool AnyRunning(Func<string, bool>? filter = null)
    {
        lock (_lock)
            return _running.Values.Any(p => !p.HasExited && (filter == null || filter(p.Key)));
    }

    private async Task WatchAsync(RunningProcess running)
    {
        var code = await running.Exited;
        Exited?.Invoke(running, code);
    }
}
