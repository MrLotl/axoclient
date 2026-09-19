using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace McLauncher;

/// <summary>Ein veröffentlichtes Release auf GitHub mit der herunterladbaren AxoClient.exe.</summary>
public record UpdateInfo(string Version, string Notes, string DownloadUrl, long Size, string PageUrl);

/// <summary>
/// Updates über GitHub Releases: Beim Start wird das neueste Release (auch Alpha/Beta) mit der eigenen Version
/// verglichen. Installiert wird, indem die laufende .exe umbenannt, die neue an ihre Stelle gelegt und neu
/// gestartet wird (Windows erlaubt das Umbenennen laufender Programme, nicht aber das Überschreiben).
/// </summary>
public static class Updater
{
    private const string AssetName = "AxoClient.exe";

    /// <summary>Eigene Version, z.B. "0.2.0-alpha.1" (lokale Builds: "0.0.0-dev").</summary>
    public static string CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0-dev";

    /// <summary>"besitzer/name" des GitHub-Repositorys; wird beim Release-Build eingetragen.</summary>
    private static string? Repo { get; } = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdateRepo")?.Value is { Length: > 0 } repo ? repo : null;

    /// <summary>Nur Release-Builds von GitHub suchen nach Updates (nicht die Entwicklungsversion aus Visual Studio).</summary>
    public static bool IsEnabled => Repo != null && CurrentVersion != "0.0.0-dev" && Environment.ProcessPath != null;

    private static string ExePath => Environment.ProcessPath!;
    private static string OldExePath => Path.ChangeExtension(ExePath, ".old.exe");
    private static string NewExePath => Path.ChangeExtension(ExePath, ".new.exe");

    /// <summary>Räumt die alte .exe vom letzten Update weg (beim Start aufrufen).</summary>
    public static void CleanUp()
    {
        if (Environment.ProcessPath == null)
            return;
        foreach (var file in new[] { OldExePath, NewExePath })
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // die alte Version läuft noch kurz; beim nächsten Start erneut
            }
        }
    }

    /// <summary>Neuere Version auf GitHub oder null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http)
    {
        if (!IsEnabled)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases?per_page=10");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AxoClient", "1.0"));
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
            if (asset.ValueKind != JsonValueKind.Object || Compare(version, best?.Version ?? CurrentVersion) <= 0)
                continue;
            best = new UpdateInfo(version,
                release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                release.GetProperty("html_url").GetString()!);
        }
        return best;
    }

    /// <summary>Lädt das Update herunter, tauscht die .exe aus und startet die neue Version.</summary>
    public static async Task InstallAsync(HttpClient http, UpdateInfo update, IProgress<double> progress)
    {
        using (var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(NewExePath);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0)
                    progress.Report(done * 100.0 / total);
            }
        }

        File.Delete(OldExePath);
        File.Move(ExePath, OldExePath);      // laufende .exe beiseite legen ...
        try
        {
            File.Move(NewExePath, ExePath);  // ... und die neue an ihre Stelle
        }
        catch
        {
            File.Move(OldExePath, ExePath);  // zurückrollen, damit der Launcher startbar bleibt
            throw;
        }
        Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true });
    }

    /// <summary>Vergleicht Versionen wie "0.2.0", "0.2.0-alpha.3" (Vorabversionen sind älter als die fertige).</summary>
    public static int Compare(string a, string b)
    {
        static (Version number, string[] pre) Parse(string v)
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
            return pb.Length.CompareTo(pa.Length); // ohne Zusatz ist neuer
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
