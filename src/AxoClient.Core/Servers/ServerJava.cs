using System.IO.Compression;
using System.Text.Json;
using AxoClient.Core.Common;
using AxoClient.Core.Game;

namespace AxoClient.Core.Servers;

/// <summary>Java für lokale Server: vorhandene passende Runtime oder Eclipse Temurin JRE von Adoptium (mit SHA-256).</summary>
public sealed class ServerJava(AppPaths paths, ApiClient api, DownloadManager downloads, JavaManager java)
{
    private string Folder(int major) => Path.Combine(paths.Java, $"server-jre-{major}");

    public async Task<string> EnsureAsync(int required, JobContext job)
    {
        var major = required == 16 ? 17 : required;
        if (FindOwn(major) is { } own)
            return own;
        foreach (var runtime in await java.FindAllAsync(job.Token))
        {
            if (major == 8 ? runtime.Major == 8 : runtime.Major >= major)
            {
                var exe = Path.Combine(Path.GetDirectoryName(runtime.Path)!, "java.exe");
                if (File.Exists(exe))
                    return exe;
            }
        }

        job.Status("servers.downloadingJava", ("major", major));
        using var assets = JsonDocument.Parse(await api.GetStringAsync(
            $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse",
            TimeSpan.FromHours(6), job.Token));
        var package = assets.RootElement.EnumerateArray()
            .Select(a => a.GetProperty("binary").GetProperty("package"))
            .FirstOrDefault(p => p.GetProperty("name").GetString()!.EndsWith(".zip"));
        if (package.ValueKind != JsonValueKind.Object)
            throw new AxoException("servers.javaUnavailable", new() { ["major"] = major });

        var zip = Path.Combine(paths.Tmp, $"server-jre-{major}.zip");
        await downloads.DownloadAsync(new DownloadRequest(package.GetProperty("link").GetString()!, zip,
            Sha256: package.GetProperty("checksum").GetString(), Size: package.GetProperty("size").GetInt64()), job.Range(0, 0.9), job.Token);
        try
        {
            var target = Folder(major);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            job.Status("servers.extractingJava");
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(zip);
                foreach (var entry in archive.Entries)
                {
                    var path = PathGuard.SafeCombine(target, entry.FullName.TrimEnd('/'));
                    if (path == null)
                        continue;
                    if (entry.FullName.EndsWith('/'))
                        Directory.CreateDirectory(path);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        entry.ExtractToFile(path, overwrite: true);
                    }
                }
            }, job.Token);
        }
        finally
        {
            File.Delete(zip);
        }
        return FindOwn(major) ?? throw new AxoException("servers.javaUnavailable", new() { ["major"] = major });
    }

    private string? FindOwn(int major)
    {
        var dir = Folder(major);
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "java.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4 })
                .FirstOrDefault()
            : null;
    }
}
