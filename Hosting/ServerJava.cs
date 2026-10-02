using System.IO.Compression;

namespace AxoClient.Hosting;

public static class ServerJava
{
    private static string Folder(int major) => Path.Combine(AppPaths.Runtime, $"server-jre-{major}");

    public static async Task<string> EnsureAsync(HttpClient http, int required, WorkProgress progress)
    {
        var major = required == 16 ? 17 : required;
        if (Find(major) is { } existing)
            return existing;

        progress.Text.Report($"Lade Java {major} für den Server …");
        var zip = Path.Combine(AppPaths.Runtime, $"server-jre-{major}.zip");
        var url = $"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/x64/jre/hotspot/normal/eclipse";
        long done = 0;
        await HttpDownloads.DownloadToFileAsync(http, url, zip, bytes =>
        {
            done += bytes;
            progress.Text.Report($"Lade Java {major} für den Server … {done / 1048576.0:0} MB");
        }, progress.Cancel);

        progress.Text.Report($"Entpacke Java {major} …");
        var target = Folder(major);
        try
        {
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, target), progress.Cancel);
        }
        finally
        {
            FileOps.TryDelete(zip);
        }
        JavaRuntimes.ClearCache();
        return Find(major) ?? throw new InvalidOperationException($"Java {major} konnte nicht eingerichtet werden.");
    }

    private static string? Find(int major)
    {
        if (Directory.Exists(Folder(major))
            && FileOps.EnumerateFilesSafe(Folder(major), "java.exe", maxDepth: 4).FirstOrDefault() is { } own)
            return own;
        var match = JavaRuntimes.FindAllCached()
            .Where(j => major == 8 ? j.Major == 8 : j.Major >= major)
            .OrderBy(j => j.Major != major)
            .ThenBy(j => j.Major)
            .Select(j => Path.Combine(Path.GetDirectoryName(j.Path)!, "java.exe"))
            .FirstOrDefault(File.Exists);
        return match;
    }
}
