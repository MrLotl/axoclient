using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace AxoClient.Core;

public static class ErrorReport
{
    public const string Fallback = "Es ist ein Fehler aufgetreten.";

    private const int FileInUse = unchecked((int)0x80070020);
    private const int DiskFull = unchecked((int)0x80070070);

    private static readonly object LogLock = new();

    public static string LogPath => AppPaths.ErrorLog;

    public static string Describe(Exception? ex)
    {
        if (ex == null)
            return Fallback;

        var parts = new List<string>();
        foreach (var current in Chain(ex))
        {
            var text = Sentence(current);
            if (text.Length > 0 && !parts.Any(p => p.Contains(text, StringComparison.OrdinalIgnoreCase)
                                                   || text.Contains(p, StringComparison.OrdinalIgnoreCase)))
                parts.Add(text);
        }

        if (parts.Count == 0)
            return Unknown(ex);

        var message = parts[0];
        if (parts.Count > 1)
            message += "\n\nUrsache: " + string.Join("\nUrsache: ", parts.Skip(1));
        if (Hint(ex) is { Length: > 0 } hint)
            message += "\n\n" + hint;
        return message;
    }

    public static string Short(Exception? ex)
    {
        if (ex == null)
            return Fallback;
        var text = Chain(ex).Select(Sentence).FirstOrDefault(s => s.Length > 0) ?? Unknown(ex);
        return string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
    }

    public static string Details(Exception? ex, string? context = null)
    {
        var text = new StringBuilder();
        text.Append(AppInfo.Name).Append(' ').Append(AppInfo.Version)
            .Append(" - ").Append(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")).Append('\n');
        if (!string.IsNullOrWhiteSpace(context))
            text.Append(context).Append('\n');
        text.Append(ex?.ToString() ?? "(keine Ausnahme)");
        return text.ToString();
    }

    public static void Log(string context, Exception? ex)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                TrimLog();
                File.AppendAllText(LogPath, Details(ex, context) + "\n\n----\n\n", Encoding.UTF8);
            }
        }
        catch
        {
        }
    }

    private static void TrimLog()
    {
        var file = new FileInfo(LogPath);
        if (!file.Exists || file.Length < 1_000_000)
            return;
        var text = File.ReadAllText(LogPath, Encoding.UTF8);
        File.WriteAllText(LogPath, text[(text.Length / 2)..], Encoding.UTF8);
    }

    private static string Unknown(Exception ex) => $"{Fallback} ({ex.GetType().Name})";

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        var seen = 0;
        var current = ex;
        while (current != null && seen++ < 5)
        {
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions.Take(3))
                    foreach (var deeper in Chain(inner))
                        yield return deeper;
                yield break;
            }
            yield return current;
            current = current.InnerException;
        }
    }

    private static string Sentence(Exception ex) => ex switch
    {
        HttpRequestException http =>
            "Die Verbindung ist fehlgeschlagen"
            + (http.StatusCode is { } status ? $" (Status {(int)status} {status})" : "")
            + ": " + Clean(http.Message, ex),
        TaskCanceledException or TimeoutException => "Die Anfrage hat zu lange gebraucht und wurde abgebrochen.",
        OperationCanceledException => "Der Vorgang wurde abgebrochen.",
        UnauthorizedAccessException => "Kein Zugriff: " + Clean(ex.Message, ex),
        FileNotFoundException notFound => "Eine Datei fehlt: " + (notFound.FileName ?? Clean(notFound.Message, ex)),
        DirectoryNotFoundException => "Ein Ordner fehlt: " + Clean(ex.Message, ex),
        PathTooLongException =>
            "Der Pfad ist zu lang für Windows. Lege den Spielordner näher an den Laufwerksanfang, z.B. C:\\AxoClient.",
        IOException io => io.HResult switch
        {
            FileInUse => "Eine Datei ist gerade in Benutzung: " + Clean(io.Message, ex),
            DiskFull => "Auf dem Laufwerk ist kein Platz mehr frei.",
            _ => Clean(io.Message, ex)
        },
        JsonException json =>
            "Die Daten haben ein unerwartetes Format"
            + (json.LineNumber is { } line ? $" (Zeile {line + 1})" : "") + ": " + Clean(json.Message, ex),
        Win32Exception win32 => Clean(win32.Message, ex) + $" (Windows-Fehler {win32.NativeErrorCode})",
        _ => Clean(ex.Message, ex)
    };

    private static string? Hint(Exception ex)
    {
        var chain = Chain(ex).ToList();
        if (chain.Any(e => e is HttpRequestException or TaskCanceledException))
            return "Prüfe deine Internetverbindung und versuche es noch einmal.";
        if (chain.Any(e => e is UnauthorizedAccessException || (e is IOException && e.HResult == FileInUse)))
            return "Läuft Minecraft noch? Dann sind manche Dateien gesperrt. Sonst kann auch ein Virenscanner blockieren.";
        return null;
    }

    private static string Clean(string? message, Exception ex)
    {
        message = message?.Trim();
        return string.IsNullOrEmpty(message) || IsNoise(message) ? Unknown(ex) : message;
    }

    private static bool IsNoise(string message) =>
        message.StartsWith("Object reference not set", StringComparison.OrdinalIgnoreCase)
        || message.StartsWith("Exception of type", StringComparison.OrdinalIgnoreCase)
        || message.StartsWith("Der Objektverweis", StringComparison.OrdinalIgnoreCase)
        || message.StartsWith("Ausnahme vom Typ", StringComparison.OrdinalIgnoreCase);
}
