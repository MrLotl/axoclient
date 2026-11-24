namespace AxoClient.Common
{
    public interface IAppLog
    {
        void Initialize(string logsDirectory);

        void Info(string message);

        void Warn(string message, Exception? exception = null);

        void Error(string message, Exception? exception = null);

        string? CurrentFile { get; }

        /// <summary>Löscht Launcher-Logs, die älter als <paramref name="days"/> Tage sind.</summary>
        void Prune(int days = 14);
    }
}
