using System.Security.Cryptography;
using Microsoft.VisualBasic.FileIO;

namespace AxoClient.Core;

public static class FileOps
{
    public static string? SafeCombine(string baseDir, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 240)
            return null;
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Contains('\0'))
            return null;

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.IndexOfAny(invalid) >= 0
                || segment.EndsWith(' ') || segment.EndsWith('.'))
                return null;
        }

        return ResolveInside(baseDir, normalized);
    }

    public static string? ResolveInside(string parent, string relative)
    {
        try
        {
            var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void CopyDirectory(string source, string target, bool overwrite)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            if (overwrite || !File.Exists(destination))
                File.Copy(file, destination, overwrite);
        }
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)), overwrite);
    }

    public static string UniqueDirectory(string parent, string name)
    {
        var target = Path.Combine(parent, name);
        for (var i = 2; Directory.Exists(target) || File.Exists(target); i++)
            target = Path.Combine(parent, $"{name} ({i})");
        return target;
    }

    public static bool TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorReport.Log($"\"{path}\" löschen", ex);
        }
        return false;
    }

    public static void Recycle(string path)
    {
        if (Directory.Exists(path))
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    public static string Sha1(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    public static long DirectorySize(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? new DirectoryInfo(dir).EnumerateFiles("*", System.IO.SearchOption.AllDirectories).Sum(f => f.Length)
                : 0;
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"Größe von \"{dir}\" bestimmen", ex);
            return 0;
        }
    }

    public static int CountEntries(string dir, string pattern = "*")
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetFileSystemEntries(dir, pattern).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorReport.Log($"Dateien in \"{dir}\" zählen", ex);
            return 0;
        }
    }

    public static List<string> Files(string dir, string pattern = "*")
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static List<string> EntryNames(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.GetFileSystemEntries(dir)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Where(n => n.Length > 0 && !n.StartsWith('.'))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IEnumerable<string> EnumerateFilesSafe(string dir, string pattern, int maxDepth = int.MaxValue)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = maxDepth
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IEnumerable<string> EnumerateDirectoriesSafe(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
