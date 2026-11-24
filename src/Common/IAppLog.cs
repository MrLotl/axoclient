namespace AxoClient.Common
{
    public interface IAppLog
    {
        void Initialize(string logsDir);

        void Info(string message);

        void Warn(string message, Exception? ex = null);

        void Error(string message, Exception? ex = null);

        string? CurrentFile { get; }

        /// <summary>Löscht Launcher-Logs, die älter als <paramref name="days"/> Tage sind.</summary>
        void Prune(int days = 14);
    }
}
