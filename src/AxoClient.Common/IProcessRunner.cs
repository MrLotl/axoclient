using System.Diagnostics;

namespace AxoClient.Common
{
    public interface IProcessRunner
    {
        event Action<RunningProcess>? Started;

        event Action<RunningProcess, int>? Exited;

        RunningProcess Start(string key, ProcessStartInfo info);

        RunningProcess? Get(string key);

        /// <summary>Auch bereits beendete Prozesse, damit die Konsole das letzte Log noch zeigt.</summary>
        RunningProcess? GetIncludingExited(string key);

        IReadOnlyList<RunningProcess> All { get; }

        bool AnyRunning(Func<string, bool>? filter = null);
    }
}
