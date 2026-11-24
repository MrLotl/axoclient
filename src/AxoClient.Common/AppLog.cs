using System.Collections.Concurrent;
using System.Text;

namespace AxoClient.Common
{
    /// <summary>
    /// Schlichtes Datei-Log des Launchers in <c>logs/launcher-JJJJ-MM-TT.log</c>. Alle Zeilen laufen durch den
    /// <see cref="LogRedactor"/>, Tokens landen also nie im Klartext auf der Platte.
    /// </summary>
    public class AppLog : IAppLog
    {
        private readonly ILogRedactor _logRedactor;
        private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), 10_000);
        private string? _dir;
        private Thread? _writer;

        public AppLog(ILogRedactor logRedactor)
        {
            _logRedactor = logRedactor;
        }

        public void Initialize(string logsDir)
        {
            if (_writer != null)
            {
                return;
            }

            _dir = logsDir;
            Directory.CreateDirectory(logsDir);
            _writer = new Thread(writeLoop) { IsBackground = true, Name = "AppLog" };
            _writer.Start();
        }

        public void Info(string message) => write("INFO", message, null);
        public void Warn(string message, Exception? ex = null) => write("WARN", message, ex);
        public void Error(string message, Exception? ex = null) => write("ERROR", message, ex);

        public string? CurrentFile => _dir == null ? null : Path.Combine(_dir, $"launcher-{DateTime.Now:yyyy-MM-dd}.log");

        private void write(string level, string message, Exception? ex)
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ').Append(level).Append(' ')
                .Append(message);
            if (ex != null)
            {
                line.AppendLine().Append(ex);
            }

            var text = _logRedactor.Redact(line.ToString());
            System.Diagnostics.Debug.WriteLine(text);
            if (_dir != null)
            {
                _queue.TryAdd(text);
            }
        }

        private void writeLoop()
        {
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                try
                {
                    File.AppendAllText(CurrentFile!, line + Environment.NewLine);
                }
                catch (IOException)
                {
                    // Log ist nie wichtiger als der Launcher selbst
                }
            }
        }

        /// <summary>Löscht Launcher-Logs, die älter als <paramref name="days"/> Tage sind.</summary>
        public void Prune(int days = 14)
        {
            if (_dir == null || !Directory.Exists(_dir))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_dir, "launcher-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-days))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
