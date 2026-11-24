using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;

namespace AxoClient.Game
{
    /// <summary>
    /// Java-Runtimes: Mojangs Runtimes lädt CmlLib automatisch nach <c>java/</c>. Hier geht es um das Finden,
    /// Prüfen (<c>java -version</c>) und die Frage, welche Hauptversion eine MC-Version braucht.
    /// </summary>
    public sealed partial class JavaManager : IJavaManager
    {
        private readonly AppPaths _paths;
        private readonly IGameVersionHelper _gameVersionHelper;
        private readonly string[] _vendors = ["Java", "Eclipse Adoptium", "Zulu", "Microsoft", "Amazon Corretto", "BellSoft"];

        public JavaManager(AppPaths paths, IGameVersionHelper gameVersionHelper)
        {
            _paths = paths;
            _gameVersionHelper = gameVersionHelper;
        }

        /// <summary>Welche Java-Hauptversion braucht diese MC-Version? Erst aus dem Versions-JSON, sonst geschätzt.</summary>
        public int RequiredMajor(string versionId) => readRequiredMajor(versionId, 0) ?? _gameVersionHelper.RequiredJava(versionId);

        private int? readRequiredMajor(string versionId, int depth)
        {
            if (depth > 4 || !_gameVersionHelper.IsSafeVersion(versionId))
            {
                return null;
            }

            var file = Path.Combine(_paths.Versions, versionId, versionId + ".json");
            if (!File.Exists(file))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("javaVersion", out var java)
                    && java.TryGetProperty("majorVersion", out var major) && major.TryGetInt32(out var value))
                {
                    return value;
                }

                if (doc.RootElement.TryGetProperty("inheritsFrom", out var parent) && parent.GetString() is { } parentId)
                {
                    return readRequiredMajor(parentId, depth + 1);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
            }
            return null;
        }

        /// <summary>Alle gefundenen Runtimes: mitgelieferte zuerst, dann installierte.</summary>
        public async Task<IReadOnlyList<JavaRuntime>> FindAll(CancellationToken ct = default)
        {
            var candidates = new List<(string Path, string Source)>();
            if (Directory.Exists(_paths.Java))
            {
                candidates.AddRange(Directory.EnumerateFiles(_paths.Java, "javaw.exe", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 6,
                    IgnoreInaccessible = true
                }).Select(p => (p, "bundled")));
            }

            if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } home)
            {
                candidates.Add((Path.Combine(home, "bin", "javaw.exe"), "javaHome"));
            }

            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     }.Where(r => r.Length > 0).Distinct())
            {
                foreach (var vendor in _vendors)
                {
                    var dir = Path.Combine(root, vendor);
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }

                    try
                    {
                        candidates.AddRange(Directory.EnumerateDirectories(dir)
                            .Select(d => Path.Combine(d, "bin", "javaw.exe"))
                            .Where(File.Exists)
                            .Select(p => (p, "system")));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }

            var found = new List<JavaRuntime>();
            foreach (var (path, source) in candidates.DistinctBy(c => c.Path.ToLowerInvariant()))
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                found.Add(new JavaRuntime(path, await ReadMajor(path, ct) ?? 0, source));
            }
            return found;
        }

        /// <summary>Startet <c>java -version</c> und liest die Hauptversion. <c>null</c> = nicht startbar.</summary>
        public async Task<int?> ReadMajor(string javawPath, CancellationToken ct = default)
        {
            var exe = Path.Combine(Path.GetDirectoryName(javawPath) ?? "", "java.exe");
            if (!File.Exists(exe))
            {
                exe = javawPath;
            }

            if (!File.Exists(exe))
            {
                return null;
            }

            try
            {
                using var process = Process.Start(new ProcessStartInfo(exe, "-version")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process == null)
                {
                    return null;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(6));
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                return ParseMajor(await stderr + await stdout);
            }
            catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception or IOException)
            {
                return null;
            }
        }

        internal int? ParseMajor(string versionOutput)
        {
            var match = versionLine().Match(versionOutput);
            if (!match.Success)
            {
                return null;
            }

            var first = int.Parse(match.Groups[1].Value);
            return first == 1 && match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : first;
        }

        /// <summary>Passt ein eigener Java-Pfad zur MC-Version? (Start-Pipeline: Warnung)</summary>
        public bool IsCompatible(int javaMajor, int required) =>
            required <= 8 ? javaMajor == 8 : javaMajor >= required;

        [GeneratedRegex(@"version ""(\d+)(?:\.(\d+))?")]
        private partial Regex versionLine();
    }
}
