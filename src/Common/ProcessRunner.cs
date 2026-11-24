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

        public RunningProcess Start(string key, ProcessStartInfo processStartInfo)
        {
            processStartInfo.UseShellExecute = false;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.RedirectStandardInput = true;
            processStartInfo.CreateNoWindow = true;
            processStartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            processStartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;

            var running = new RunningProcess(key, new Process { StartInfo = processStartInfo }, _logRedactor);
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
                return _running.TryGetValue(key, out var value) && !value.HasExited ? value : null;
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
                return _running.Values.Any(value => !value.HasExited && (filter == null || filter(value.Key)));
            }
        }

        private async Task watch(RunningProcess running)
        {
            var code = await running.Exited;
            Exited?.Invoke(running, code);
        }
    }
}
