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

        public ServerImportInfo Inspect(string directory)
        {
            if (!Directory.Exists(directory))
            {
                throw new AxoException("errors.notFound");
            }

            var jars = Directory.EnumerateFiles(directory, "*.jar").Select(Path.GetFileName).OfType<string>().ToList();
            var hasWorld = Directory.EnumerateDirectories(directory).Any(subDirectory => File.Exists(Path.Combine(subDirectory, "level.dat")));
            if (jars.Count == 0 && !File.Exists(Path.Combine(directory, "server.properties")) && !File.Exists(Path.Combine(directory, "velocity.toml")) && !hasWorld)
            {
                throw new AxoException("localServers.import.notAServer");
            }

            var names = string.Join(" ", jars).ToLowerInvariant();
            var software = names.Contains("velocity") || File.Exists(Path.Combine(directory, "velocity.toml")) ? ServerSoftware.Velocity
                : names.Contains("paper") || names.Contains("purpur") || File.Exists(Path.Combine(directory, "bukkit.yml")) || File.Exists(Path.Combine(directory, "spigot.yml")) ? ServerSoftware.Paper
                : names.Contains("fabric") || Directory.Exists(Path.Combine(directory, ".fabric")) ? ServerSoftware.Fabric
                : ServerSoftware.Vanilla;

            var properties = readProperties(Path.Combine(directory, "server.properties"));
            var eula = File.Exists(Path.Combine(directory, "eula.txt"))
                       && File.ReadLines(Path.Combine(directory, "eula.txt")).Any(line => line.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase));
            return new ServerImportInfo(
                _textHelper.Clean(Path.GetFileName(directory.TrimEnd('\\', '/')), 60) is { Length: > 0 } folderName ? folderName : "Server",
                software,
                detectVersion(directory, jars),
                int.TryParse(properties.GetValueOrDefault("server-port"), out var port) && port is >= 1024 and <= 65535 ? port : 25565,
                int.TryParse(properties.GetValueOrDefault("max-players"), out var max) && max is >= 1 and <= 500 ? max : 20,
                eula,
                Directory.EnumerateDirectories(directory).Count(subDirectory => File.Exists(Path.Combine(subDirectory, "level.dat"))),
                count(Path.Combine(directory, "plugins"), "*.jar"),
                count(Path.Combine(directory, "mods"), "*.jar"));
        }

        /// <summary>Version aus <c>version.json</c> in einer Jar, sonst aus dem Dateinamen (paper-1.21.4-123.jar, …mc.1.21.4-…).</summary>
        private string? detectVersion(string directory, List<string> jars)
        {
            foreach (var jar in jars)
            {
                try
                {
                    using var zip = ZipFile.OpenRead(Path.Combine(directory, jar));
                    if (zip.GetEntry("version.json") is { } entry)
                    {
                        using var stream = entry.Open();
                        using var document = JsonDocument.Parse(stream);
                        if (document.RootElement.TryGetProperty("id", out var id) && id.GetString() is { } version && _gameVersionHelper.IsSafeVersion(version))
                        {
                            return version;
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
                {
                }
            }
            foreach (var jar in jars)
            {
                if (versionInName().Match(jar) is { Success: true } match)
                {
                    return match.Groups[1].Value;
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
            for (var index = 0; index < entries.Count; index++)
            {
                job.Token.ThrowIfCancellationRequested();
                job.Report((double)index / Math.Max(1, entries.Count));
                var name = Path.GetFileName(entries[index]);
                if (_skipped.Contains(name) || (File.Exists(entries[index]) && (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                                                            || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))))
                {
                    continue;
                }

                failed += copyEntry(entries[index], Path.Combine(to, name));
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
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _appLog.Warn($"Server-Import: {Path.GetFileName(from)} nicht kopierbar", exception);
                return false;
            }
        }

        private int count(string directory, string pattern) => Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern).Count() : 0;

        private Dictionary<string, string> readProperties(string file)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(file))
            {
                return result;
            }

            foreach (var line in File.ReadLines(file))
            {
                var equalsIndex = line.IndexOf('=');
                if (equalsIndex > 0 && !line.TrimStart().StartsWith('#'))
                {
                    result[line[..equalsIndex].Trim()] = line[(equalsIndex + 1)..].Trim();
                }
            }
            return result;
        }

        [GeneratedRegex(@"(?:^|[-_.])(?:mc\.)?(\d+\.\d+(?:\.\d+)?)(?=[-_.])")]
        private partial Regex versionInName();
    }
}
