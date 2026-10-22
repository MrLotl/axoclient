using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Core.Common;

namespace AxoClient.Core.Servers;

/// <summary>Was aus einem vorhandenen Server-Ordner erkannt wurde – die UI zeigt es im Server-Dialog zur Kontrolle.</summary>
public sealed record ServerImportInfo(string Name, ServerSoftware Software, string? Version, int Port, int MaxPlayers, bool EulaAccepted,
    int Worlds, int Plugins, int Mods);

/// <summary>
/// Übernimmt einen vorhandenen Minecraft-Server (z. B. aus dem alten Launcher oder selbst aufgesetzt).
/// Kopiert wird alles außer Server-Jars, Bibliotheken, Caches und Logs; der Originalordner bleibt unverändert.
/// Die passende Server-Jar lädt AxoClient beim ersten Start selbst.
/// </summary>
public static partial class ServerImport
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "libraries", "versions", "cache", "logs", "crash-reports", ".fabric", "bundler", "session.lock", "axoclient-server.json"
    };

    public static ServerImportInfo Inspect(string dir)
    {
        if (!Directory.Exists(dir))
            throw new AxoException("errors.notFound");
        var jars = Directory.EnumerateFiles(dir, "*.jar").Select(Path.GetFileName).OfType<string>().ToList();
        var hasWorld = Directory.EnumerateDirectories(dir).Any(d => File.Exists(Path.Combine(d, "level.dat")));
        if (jars.Count == 0 && !File.Exists(Path.Combine(dir, "server.properties")) && !File.Exists(Path.Combine(dir, "velocity.toml")) && !hasWorld)
            throw new AxoException("localServers.import.notAServer");

        var names = string.Join(" ", jars).ToLowerInvariant();
        var software = names.Contains("velocity") || File.Exists(Path.Combine(dir, "velocity.toml")) ? ServerSoftware.Velocity
            : names.Contains("paper") || names.Contains("purpur") || File.Exists(Path.Combine(dir, "bukkit.yml")) || File.Exists(Path.Combine(dir, "spigot.yml")) ? ServerSoftware.Paper
            : names.Contains("fabric") || Directory.Exists(Path.Combine(dir, ".fabric")) ? ServerSoftware.Fabric
            : ServerSoftware.Vanilla;

        var props = ReadProperties(Path.Combine(dir, "server.properties"));
        var eula = File.Exists(Path.Combine(dir, "eula.txt"))
                   && File.ReadLines(Path.Combine(dir, "eula.txt")).Any(l => l.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase));
        return new ServerImportInfo(
            Text.Clean(Path.GetFileName(dir.TrimEnd('\\', '/')), 60) is { Length: > 0 } n ? n : "Server",
            software,
            DetectVersion(dir, jars),
            int.TryParse(props.GetValueOrDefault("server-port"), out var port) && port is >= 1024 and <= 65535 ? port : 25565,
            int.TryParse(props.GetValueOrDefault("max-players"), out var max) && max is >= 1 and <= 500 ? max : 20,
            eula,
            Directory.EnumerateDirectories(dir).Count(d => File.Exists(Path.Combine(d, "level.dat"))),
            Count(Path.Combine(dir, "plugins"), "*.jar"),
            Count(Path.Combine(dir, "mods"), "*.jar"));
    }

    /// <summary>Version aus <c>version.json</c> in einer Jar, sonst aus dem Dateinamen (paper-1.21.4-123.jar, …mc.1.21.4-…).</summary>
    private static string? DetectVersion(string dir, List<string> jars)
    {
        foreach (var jar in jars)
        {
            try
            {
                using var zip = ZipFile.OpenRead(Path.Combine(dir, jar));
                if (zip.GetEntry("version.json") is { } entry)
                {
                    using var stream = entry.Open();
                    using var doc = JsonDocument.Parse(stream);
                    if (doc.RootElement.TryGetProperty("id", out var id) && id.GetString() is { } v && Game.LoaderManager.IsSafeVersion(v))
                        return v;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
            }
        }
        foreach (var jar in jars)
            if (VersionInName().Match(jar) is { Success: true } m)
                return m.Groups[1].Value;
        return null;
    }

    /// <summary>Kopiert den Serverinhalt in den Datenordner des neuen Servers. Gibt die Zahl nicht kopierbarer Dateien zurück.</summary>
    public static int CopyInto(string from, string to, JobContext job)
    {
        Directory.CreateDirectory(to);
        var entries = Directory.EnumerateFileSystemEntries(from).ToList();
        var failed = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            job.Token.ThrowIfCancellationRequested();
            job.Report((double)i / Math.Max(1, entries.Count));
            var name = Path.GetFileName(entries[i]);
            if (Skipped.Contains(name) || (File.Exists(entries[i]) && (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                                                        || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))))
                continue;
            failed += CopyEntry(entries[i], Path.Combine(to, name));
        }
        return failed;
    }

    private static int CopyEntry(string from, string to)
    {
        if (File.Exists(from))
            return TryCopy(from, to) ? 0 : 1;
        if (new DirectoryInfo(from).LinkTarget != null)
            return 0;
        Directory.CreateDirectory(to);
        var failed = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
            if (!Path.GetFileName(entry).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                failed += CopyEntry(entry, Path.Combine(to, Path.GetFileName(entry)));
        return failed;
    }

    private static bool TryCopy(string from, string to)
    {
        try
        {
            using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(to, FileMode.Create, FileAccess.Write);
            input.CopyTo(output);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Server-Import: {Path.GetFileName(from)} nicht kopierbar", ex);
            return false;
        }
    }

    private static int Count(string dir, string pattern) => Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern).Count() : 0;

    private static Dictionary<string, string> ReadProperties(string file)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(file))
            return result;
        foreach (var line in File.ReadLines(file))
        {
            var eq = line.IndexOf('=');
            if (eq > 0 && !line.TrimStart().StartsWith('#'))
                result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }

    [GeneratedRegex(@"(?:^|[-_.])(?:mc\.)?(\d+\.\d+(?:\.\d+)?)(?=[-_.])")]
    private static partial Regex VersionInName();
}
