using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AxoClient.Game;

public record JavaRuntime(string Path, int Major, string Label)
{
    public string VersionText => Major > 0 ? $"Java {Major}" : "Java (Version unbekannt)";

    public string Display => $"{VersionText}  ·  {Label}";

    public override string ToString() => Display;
}

public static class JavaRuntimes
{
    private const string BundledLabel = "Von Minecraft mitgeliefert";

    private static readonly string[] Vendors = ["Java", "Eclipse Adoptium", "Zulu", "Microsoft", "Amazon Corretto", "BellSoft"];

    private static List<JavaRuntime>? _cached;

    public static List<JavaRuntime> FindAllCached() => _cached ??= FindAll();

    public static void ClearCache() => _cached = null;

    public static List<JavaRuntime> FindAll()
    {
        var found = new List<JavaRuntime>();
        foreach (var candidate in BundledPaths().Concat(SystemPaths()))
        {
            if (!File.Exists(candidate.Path) || found.Any(f => FileOps.SamePath(f.Path, candidate.Path)))
                continue;
            found.Add(candidate with { Major = ReadMajor(candidate.Path) });
        }
        return found
            .OrderByDescending(j => j.Label.StartsWith(BundledLabel))
            .ThenByDescending(j => j.Major)
            .ToList();
    }

    private static IEnumerable<JavaRuntime> BundledPaths()
    {
        if (!Directory.Exists(AppPaths.Runtime))
            yield break;
        foreach (var exe in FileOps.EnumerateFilesSafe(AppPaths.Runtime, "javaw.exe", maxDepth: 6))
        {
            var component = Path.GetRelativePath(AppPaths.Runtime, exe).Split(Path.DirectorySeparatorChar)[0];
            yield return new JavaRuntime(exe, 0, component.Length > 0 ? $"{BundledLabel} ({component})" : BundledLabel);
        }
    }

    private static IEnumerable<JavaRuntime> SystemPaths()
    {
        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } home)
            yield return new JavaRuntime(Path.Combine(home, "bin", "javaw.exe"), 0, "JAVA_HOME");

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate;
            try
            {
                if (dir.Trim().Length == 0)
                    continue;
                candidate = Path.Combine(dir.Trim(), "javaw.exe");
            }
            catch (ArgumentException ex)
            {
                ErrorReport.Log("PATH-Eintrag auswerten", ex);
                continue;
            }
            yield return new JavaRuntime(candidate, 0, "Im PATH");
        }

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(r => r.Length > 0).Distinct();
        foreach (var root in roots)
            foreach (var vendor in Vendors)
            {
                var vendorDir = Path.Combine(root, vendor);
                if (!Directory.Exists(vendorDir))
                    continue;
                foreach (var exe in FileOps.EnumerateDirectoriesSafe(vendorDir)
                             .Select(d => Path.Combine(d, "bin", "javaw.exe"))
                             .Where(File.Exists))
                    yield return new JavaRuntime(exe, 0, vendor);
            }
    }

    public static int RequiredMajor(Installation inst) =>
        ReadRequiredMajor(inst.MinecraftVersion) ?? GuessMajor(inst.MinecraftVersion);

    private static int? ReadRequiredMajor(string versionId, int depth = 0)
    {
        if (depth > 4 || versionId.Length == 0 || versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        var file = Path.Combine(AppPaths.Versions, versionId, versionId + ".json");
        if (!File.Exists(file))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("javaVersion", out var java)
                && java.TryGetProperty("majorVersion", out var major) && major.TryGetInt32(out var value))
                return value;
            if (doc.RootElement.TryGetProperty("inheritsFrom", out var parent) && parent.GetString() is { } parentId)
                return ReadRequiredMajor(parentId, depth + 1);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
        }
        return null;
    }

    private static int GuessMajor(string mcVersion)
    {
        var release = Regex.Match(mcVersion, @"^1\.(\d+)(?:\.(\d+))?$");
        if (!release.Success)
            return Regex.IsMatch(mcVersion, @"^\d") ? 21 : 8;
        var minor = int.Parse(release.Groups[1].Value);
        var patch = release.Groups[2].Success ? int.Parse(release.Groups[2].Value) : 0;
        if (minor > 20 || (minor == 20 && patch >= 5))
            return 21;
        return minor >= 18 ? 17 : minor >= 17 ? 16 : 8;
    }

    public static string? ResolveOverride(Installation inst, LauncherSettings settings) =>
        new[] { inst.JavaPath, settings.JavaPath }.FirstOrDefault(path => path is { Length: > 0 } && File.Exists(path));

    public static int ReadMajor(string javawPath)
    {
        var exe = Path.Combine(Path.GetDirectoryName(javawPath) ?? "", "java.exe");
        if (!File.Exists(exe))
            return ReadMajorFromRelease(javawPath);
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
                return ReadMajorFromRelease(javawPath);
            var output = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(4000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    ErrorReport.Log("Java-Abfrage beenden", ex);
                }
                return ReadMajorFromRelease(javawPath);
            }
            return ParseMajor(output) ?? ReadMajorFromRelease(javawPath);
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
        {
            return ReadMajorFromRelease(javawPath);
        }
    }

    private static int ReadMajorFromRelease(string javawPath)
    {
        try
        {
            var home = Path.GetDirectoryName(Path.GetDirectoryName(javawPath));
            var release = home == null ? null : Path.Combine(home, "release");
            if (release == null || !File.Exists(release))
                return 0;
            foreach (var line in File.ReadLines(release))
                if (line.StartsWith("JAVA_VERSION=") && ParseMajor(line) is { } major)
                    return major;
        }
        catch (IOException ex)
        {
            ErrorReport.Log("Java-Version aus der release-Datei lesen", ex);
        }
        return 0;
    }

    private static int? ParseMajor(string text)
    {
        var match = Regex.Match(text, @"(\d+)(?:\.(\d+))?[._\d]*""?");
        if (!match.Success)
            return null;
        var first = int.Parse(match.Groups[1].Value);
        return first == 1 && match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : first;
    }
}
