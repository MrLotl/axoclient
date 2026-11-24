using System.IO.Compression;
using System.Text.Json;
using AxoClient.Common;
using AxoClient.Game.Contract;
using AxoClient.Servers.Contract;

namespace AxoClient.Servers
{
    /// <summary>Java für lokale Server: vorhandene passende Runtime oder Eclipse Temurin JRE von Adoptium (mit SHA-256).</summary>
    public sealed class ServerJava : IServerJava
    {
        private readonly AppPaths _paths;
        private readonly IApiClient _api;
        private readonly IDownloadManager _downloads;
        private readonly IJavaManager _java;
        private readonly IPathGuard _pathGuard;

        public ServerJava(AppPaths paths, IApiClient api, IDownloadManager downloads, IJavaManager java, IPathGuard pathGuard)
        {
            _paths = paths;
            _api = api;
            _downloads = downloads;
            _java = java;
            _pathGuard = pathGuard;
        }

        private string folder(int major) => Path.Combine(_paths.Java, $"server-jre-{major}");

        public async Task<string> Ensure(int required, JobContext job)
        {
            var major = required == 16 ? 17 : required;
            if (findOwn(major) is { } own)
            {
                return own;
            }

            foreach (var runtime in await _java.FindAll(job.Token))
            {
                if (major == 8 ? runtime.Major == 8 : runtime.Major >= major)
                {
                    var exePath = Path.Combine(Path.GetDirectoryName(runtime.Path)!, "java.exe");
                    if (File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }

            job.Status("servers.downloadingJava", ("major", major));
            using var assets = JsonDocument.Parse(await _api.GetString(
                $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse",
                TimeSpan.FromHours(6), job.Token));
            var package = assets.RootElement.EnumerateArray()
                .Select(asset => asset.GetProperty("binary").GetProperty("package"))
                .FirstOrDefault(packageElement => packageElement.GetProperty("name").GetString()!.EndsWith(".zip"));
            if (package.ValueKind != JsonValueKind.Object)
            {
                throw new AxoException("servers.javaUnavailable", new() { ["major"] = major });
            }

            var zip = Path.Combine(_paths.Tmp, $"server-jre-{major}.zip");
            await _downloads.Download(new DownloadRequest(package.GetProperty("link").GetString()!, zip,
                Sha256: package.GetProperty("checksum").GetString(), Size: package.GetProperty("size").GetInt64()), job.Range(0, 0.9), job.Token);
            try
            {
                var target = folder(major);
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }

                job.Status("servers.extractingJava");
                await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(zip);
                    foreach (var entry in archive.Entries)
                    {
                        var path = _pathGuard.SafeCombine(target, entry.FullName.TrimEnd('/'));
                        if (path == null)
                        {
                            continue;
                        }

                        if (entry.FullName.EndsWith('/'))
                        {
                            Directory.CreateDirectory(path);
                        }
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
            return findOwn(major) ?? throw new AxoException("servers.javaUnavailable", new() { ["major"] = major });
        }

        private string? findOwn(int major)
        {
            var directory = folder(major);
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "java.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4 })
                    .FirstOrDefault()
                : null;
        }
    }
}
