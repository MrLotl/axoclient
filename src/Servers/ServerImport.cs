using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Common;
using AxoClient.DataModel.Servers;
using AxoClient.Game.Contract;
using AxoClient.Servers.Contract;

namespace AxoClient.Servers
{
    /// <summary>
    /// Übernimmt einen vorhandenen Minecraft-Server (z. B. aus dem alten Launcher oder selbst aufgesetzt).
    /// Kopiert wird alles außer Server-Jars, Bibliotheken, Caches und Logs; der Originalordner bleibt unverändert.
    /// Die passende Server-Jar lädt AxoClient beim ersten Start selbst.
    /// </summary>
    public partial class ServerImport : IServerImport
    {
        private readonly ITextHelper _textHelper;
        private readonly IAppLog _appLog;
        private readonly IGameVersionHelper _gameVersionHelper;
        private readonly HashSet<string> _skipped = new(StringComparer.OrdinalIgnoreCase)
        {
            "libraries", "versions", "cache", "logs", "crash-reports", ".fabric", "bundler", "session.lock", "axoclient-server.json"
        };

        public ServerImport(ITextHelper textHelper, IAppLog appLog, IGameVersionHelper gameVersionHelper)
        {
            _textHelper = textHelper;
            _appLog = appLog;
            _gameVersionHelper = gameVersionHelper;
        }

        public ServerImportInfo Inspect(string dir)
        {
            if (!Directory.Exists(dir))
            {
                throw new AxoException("errors.notFound");
            }

            var jars = Directory.EnumerateFiles(dir, "*.jar").Select(Path.GetFileName).OfType<string>().ToList();
            var hasWorld = Directory.EnumerateDirectories(dir).Any(d => File.Exists(Path.Combine(d, "level.dat")));
            if (jars.Count == 0 && !File.Exists(Path.Combine(dir, "server.properties")) && !File.Exists(Path.Combine(dir, "velocity.toml")) && !hasWorld)
            {
                throw new AxoException("localServers.import.notAServer");
            }

            var names = string.Join(" ", jars).ToLowerInvariant();
            var software = names.Contains("velocity") || File.Exists(Path.Combine(dir, "velocity.toml")) ? ServerSoftware.Velocity
                : names.Contains("paper") || names.Contains("purpur") || File.Exists(Path.Combine(dir, "bukkit.yml")) || File.Exists(Path.Combine(dir, "spigot.yml")) ? ServerSoftware.Paper
                : names.Contains("fabric") || Directory.Exists(Path.Combine(dir, ".fabric")) ? ServerSoftware.Fabric
                : ServerSoftware.Vanilla;

            var props = readProperties(Path.Combine(dir, "server.properties"));
            var eula = File.Exists(Path.Combine(dir, "eula.txt"))
                       && File.ReadLines(Path.Combine(dir, "eula.txt")).Any(l => l.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase));
            return new ServerImportInfo(
                _textHelper.Clean(Path.GetFileName(dir.TrimEnd('\\', '/')), 60) is { Length: > 0 } n ? n : "Server",
                software,
                detectVersion(dir, jars),
                int.TryParse(props.GetValueOrDefault("server-port"), out var port) && port is >= 1024 and <= 65535 ? port : 25565,
                int.TryParse(props.GetValueOrDefault("max-players"), out var max) && max is >= 1 and <= 500 ? max : 20,
                eula,
                Directory.EnumerateDirectories(dir).Count(d => File.Exists(Path.Combine(d, "level.dat"))),
                count(Path.Combine(dir, "plugins"), "*.jar"),
                count(Path.Combine(dir, "mods"), "*.jar"));
        }

        /// <summary>Version aus <c>version.json</c> in einer Jar, sonst aus dem Dateinamen (paper-1.21.4-123.jar, …mc.1.21.4-…).</summary>
        private string? detectVersion(string dir, List<string> jars)
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
                        if (doc.RootElement.TryGetProperty("id", out var id) && id.GetString() is { } v && _gameVersionHelper.IsSafeVersion(v))
                        {
                            return v;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
                {
                }
            }
            foreach (var jar in jars)
            {
                if (versionInName().Match(jar) is { Success: true } m)
                {
                    return m.Groups[1].Value;
                }
            }

            return null;
        }

        /// <summary>Kopiert den Serverinhalt in den Datenordner des neuen Servers. Gibt die Zahl nicht kopierbarer Dateien zurück.</summary>
        public int CopyInto(string from, string to, JobContext job)
        {
            Directory.CreateDirectory(to);
            var entries = Directory.EnumerateFileSystemEntries(from).ToList();
            var failed = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                job.Token.ThrowIfCancellationRequested();
                job.Report((double)i / Math.Max(1, entries.Count));
                var name = Path.GetFileName(entries[i]);
                if (_skipped.Contains(name) || (File.Exists(entries[i]) && (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                                                            || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))))
                {
                    continue;
                }

                failed += copyEntry(entries[i], Path.Combine(to, name));
            }
            return failed;
        }

        private int copyEntry(string from, string to)
        {
            if (File.Exists(from))
            {
                return tryCopy(from, to) ? 0 : 1;
            }

            if (new DirectoryInfo(from).LinkTarget != null)
            {
                return 0;
            }

            Directory.CreateDirectory(to);
            var failed = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(from))
            {
                if (!Path.GetFileName(entry).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                {
                    failed += copyEntry(entry, Path.Combine(to, Path.GetFileName(entry)));
                }
            }

            return failed;
        }

        private bool tryCopy(string from, string to)
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
                _appLog.Warn($"Server-Import: {Path.GetFileName(from)} nicht kopierbar", ex);
                return false;
            }
        }

        private int count(string dir, string pattern) => Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern).Count() : 0;

        private Dictionary<string, string> readProperties(string file)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(file))
            {
                return result;
            }

            foreach (var line in File.ReadLines(file))
            {
                var eq = line.IndexOf('=');
                if (eq > 0 && !line.TrimStart().StartsWith('#'))
                {
                    result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }
            }
            return result;
        }

        [GeneratedRegex(@"(?:^|[-_.])(?:mc\.)?(\d+\.\d+(?:\.\d+)?)(?=[-_.])")]
        private partial Regex versionInName();
    }
}
