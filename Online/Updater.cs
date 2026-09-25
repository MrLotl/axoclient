using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AxoClient.Online;

public record UpdateInfo(string Version, string Notes, string DownloadUrl, long Size, string PageUrl);

public static class Updater
{
    private const string AssetName = "AxoClient.exe";

    public static bool IsEnabled =>
        AppInfo.UpdateRepo != null && AppInfo.Version != AppInfo.DevVersion && Environment.ProcessPath != null;

    private static string ExePath => Environment.ProcessPath!;
    private static string OldExePath => Path.ChangeExtension(ExePath, ".old.exe");
    private static string NewExePath => Path.ChangeExtension(ExePath, ".new.exe");

    public static void CleanUp()
    {
        if (Environment.ProcessPath == null)
            return;
        FileOps.TryDelete(OldExePath);
        FileOps.TryDelete(NewExePath);
    }

    public static async Task<UpdateInfo?> CheckAsync(HttpClient http)
    {
        if (!IsEnabled)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{AppInfo.UpdateRepo}/releases?per_page=10");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        UpdateInfo? best = null;
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean())
                continue;
            var version = release.GetProperty("tag_name").GetString()!.TrimStart('v', 'V');
            var asset = release.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("name").GetString() == AssetName);
            if (asset.ValueKind != JsonValueKind.Object || Compare(version, best?.Version ?? AppInfo.Version) <= 0)
                continue;
            best = new UpdateInfo(version,
                release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                release.GetProperty("html_url").GetString()!);
        }
        return best;
    }

    public static async Task InstallAsync(HttpClient http, UpdateInfo update, IProgress<double> percent)
    {
        long done = 0;
        await HttpDownloads.DownloadToFileAsync(http, update.DownloadUrl, NewExePath, bytes =>
        {
            done += bytes;
            if (update.Size > 0)
                percent.Report(done * 100.0 / update.Size);
        }, CancellationToken.None);

        File.Delete(OldExePath);
        File.Move(ExePath, OldExePath);
        try
        {
            File.Move(NewExePath, ExePath);
        }
        catch
        {
            File.Move(OldExePath, ExePath);
            throw;
        }
        Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true });
    }

    public static int Compare(string a, string b)
    {
        static (Version Number, string[] Pre) Parse(string v)
        {
            var parts = v.Split('+')[0].Split('-', 2);
            var number = Version.TryParse(parts[0], out var n) ? n : new Version(0, 0);
            return (number, parts.Length > 1 ? parts[1].Split('.') : []);
        }

        var (na, pa) = Parse(a);
        var (nb, pb) = Parse(b);
        var result = na.CompareTo(nb);
        if (result != 0)
            return result;
        if (pa.Length == 0 || pb.Length == 0)
            return pb.Length.CompareTo(pa.Length);
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            result = int.TryParse(pa[i], out var ia) && int.TryParse(pb[i], out var ib)
                ? ia.CompareTo(ib)
                : string.CompareOrdinal(pa[i], pb[i]);
            if (result != 0)
                return result;
        }
        return pa.Length.CompareTo(pb.Length);
    }
}
