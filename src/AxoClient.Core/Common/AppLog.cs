using System.Collections.Concurrent;
using System.Text;

namespace AxoClient.Core.Common;

/// <summary>
/// Schlichtes Datei-Log des Launchers in <c>logs/launcher-JJJJ-MM-TT.log</c>. Alle Zeilen laufen durch den
/// <see cref="LogRedactor"/>, Tokens landen also nie im Klartext auf der Platte.
/// </summary>
public static class AppLog
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 10_000);
    private static string? _dir;
    private static Thread? _writer;

    public static void Initialize(string logsDir)
    {
        if (_writer != null)
            return;
        _dir = logsDir;
        Directory.CreateDirectory(logsDir);
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "AppLog" };
        _writer.Start();
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public static string? CurrentFile => _dir == null ? null : Path.Combine(_dir, $"launcher-{DateTime.Now:yyyy-MM-dd}.log");

    private static void Write(string level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ').Append(level).Append(' ')
            .Append(message);
        if (ex != null)
            line.AppendLine().Append(ex);
        var text = LogRedactor.Redact(line.ToString());
        System.Diagnostics.Debug.WriteLine(text);
        if (_dir != null)
            Queue.TryAdd(text);
    }

    private static void WriteLoop()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
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
    public static void Prune(int days = 14)
    {
        if (_dir == null || !Directory.Exists(_dir))
            return;
        foreach (var file in Directory.EnumerateFiles(_dir, "launcher-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-days))
                    File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }
}
