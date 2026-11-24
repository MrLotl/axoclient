using System.Diagnostics;

namespace AxoClient.Common
{
    /// <summary>Startet und verwaltet Java-Prozesse von Instanzen und lokalen Servern.</summary>
    public sealed class ProcessRunner : IProcessRunner
    {
        private readonly ILogRedactor _logRedactor;
        private readonly Dictionary<string, RunningProcess> _running = new();
        private readonly Lock _lock = new();

        public ProcessRunner(ILogRedactor logRedactor)
        {
            _logRedactor = logRedactor;
        }

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

            var running = new RunningProcess(key, new Process { StartInfo = info }, _logRedactor);
            running.Start();
            lock (_lock)
            {
                _running[key] = running;
            }

            Started?.Invoke(running);
            _ = watch(running);
            return running;
        }

        public RunningProcess? Get(string key)
        {
            lock (_lock)
            {
                return _running.TryGetValue(key, out var p) && !p.HasExited ? p : null;
            }
        }

        /// <summary>Auch bereits beendete Prozesse, damit die Konsole das letzte Log noch zeigt.</summary>
        public RunningProcess? GetIncludingExited(string key)
        {
            lock (_lock)
            {
                return _running.GetValueOrDefault(key);
            }
        }

        public IReadOnlyList<RunningProcess> All
        {
            get
            {
                lock (_lock)
                {
                    return _running.Values.ToList();
                }
            }
        }

        public bool AnyRunning(Func<string, bool>? filter = null)
        {
            lock (_lock)
            {
                return _running.Values.Any(p => !p.HasExited && (filter == null || filter(p.Key)));
            }
        }

        private async Task watch(RunningProcess running)
        {
            var code = await running.Exited;
            Exited?.Invoke(running, code);
        }
    }
}
