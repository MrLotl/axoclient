using System.Diagnostics;

namespace AxoClient.Common
{
    /// <summary>
    /// Ein laufender Java-Prozess (Instanz oder lokaler Server). Sammelt Log-Zeilen in einem Ringpuffer und
    /// liefert sie live an die Konsole.
    /// </summary>
    public sealed class RunningProcess : IAsyncDisposable
    {
        private readonly ILogRedactor _logRedactor;
        private const int MaxLines = 20_000;

        private readonly Process _process;
        private readonly LinkedList<LogLine> _lines = new();
        private readonly Lock _lock = new();
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _seq;

        internal RunningProcess(string key, Process process, ILogRedactor logRedactor)
        {
            _logRedactor = logRedactor;
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
            {
                return _lines.Skip(Math.Max(0, _lines.Count - max)).ToList();
            }
        }

        /// <summary>Für die Absturzanalyse: die letzten Zeilen als Text.</summary>
        public string Tail(int count = 400)
        {
            lock (_lock)
            {
                return string.Join('\n', _lines.Skip(Math.Max(0, _lines.Count - count)).Select(line => line.Text));
            }
        }

        internal void Start()
        {
            _process.OutputDataReceived += (_, dataReceivedEventArgs) => add(dataReceivedEventArgs.Data, false);
            _process.ErrorDataReceived += (_, dataReceivedEventArgs) => add(dataReceivedEventArgs.Data, true);
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

        public async Task WriteLine(string line)
        {
            if (HasExited)
            {
                return;
            }

            await _process.StandardInput.WriteLineAsync(line);
            await _process.StandardInput.FlushAsync();
        }

        /// <summary>Beendet den Prozess. Für Server zuerst sanft per Befehl, dann hart.</summary>
        public async Task Stop(string? gracefulCommand = null, TimeSpan? grace = null)
        {
            if (HasExited)
            {
                return;
            }

            StopRequested = true;
            if (gracefulCommand != null)
            {
                try
                {
                    await WriteLine(gracefulCommand);
                    await Exited.WaitAsync(grace ?? TimeSpan.FromSeconds(30));
                    return;
                }
                catch (Exception exception) when (exception is TimeoutException or IOException or InvalidOperationException)
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

        private void add(string? text, bool stdErr)
        {
            if (text == null)
            {
                return;
            }

            text = _logRedactor.Redact(text);
            var line = new LogLine(Interlocked.Increment(ref _seq), DateTimeOffset.Now, DetectLevel(text, stdErr),
                detectThread(text), text, stdErr);
            lock (_lock)
            {
                _lines.AddLast(line);
                if (_lines.Count > MaxLines)
                {
                    _lines.RemoveFirst();
                }
            }
            LineReceived?.Invoke(line);
        }

        internal LogLevel DetectLevel(string text, bool stdErr)
        {
            // Typisches Minecraft-Format: [12:00:00] [Render thread/WARN]: ...
            if (text.Contains("/ERROR]") || text.Contains("/FATAL]") || text.Contains(" ERROR ") || text.StartsWith("Exception")
                || text.StartsWith("\tat ") || text.StartsWith("Caused by:"))
            {
                return LogLevel.Error;
            }

            if (text.Contains("/WARN]") || text.Contains(" WARN "))
            {
                return LogLevel.Warn;
            }

            if (text.Contains("/DEBUG]"))
            {
                return LogLevel.Debug;
            }

            return stdErr ? LogLevel.Warn : LogLevel.Info;
        }

        private string detectThread(string text)
        {
            var open = text.IndexOf("] [", StringComparison.Ordinal);
            if (open < 0)
            {
                return "";
            }

            var slash = text.IndexOf('/', open + 3);
            return slash > open ? text[(open + 3)..slash] : "";
        }

        public ValueTask DisposeAsync()
        {
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
