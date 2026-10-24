using System.Diagnostics;
using System.Text.Json.Serialization;
using AxoClient.Core.Common;

namespace AxoClient.Core.Updates;

public sealed record UpdateView(string Version, long SizeBytes, string? Notes, bool Downloaded);

/// <summary>
/// Selbst-Update über GitHub Releases, ohne Zusatzwerkzeug: das Release enthält eine einzige
/// <c>AxoClient-Setup.exe</c> (der Launcher selbst). Sie wird mit SHA-256-Prüfung geladen und gegen die
/// installierte <c>AxoClient.exe</c> getauscht. Kanal <c>stable</c> = neuestes normales Release,
/// <c>beta</c> = auch Vorabversionen. Nie während eine Instanz oder ein lokaler Server läuft.
/// </summary>
public sealed class UpdateService(
    SettingsService settings,
    EventHub events,
    ApiClient api,
    DownloadManager downloads,
    AppPaths paths,
    Func<bool> busy)
{
    public const string AssetName = "AxoClient-Setup.exe";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(4);
    private static readonly string ApiBase = AppInfo.Repository.Replace("https://github.com/", "https://api.github.com/repos/");

    private Release? _pending;
    private bool _downloaded;
    private CancellationTokenSource? _loop;

    private string DownloadPath => Path.Combine(paths.Updates, AssetName);

    public UpdateView? Available => _pending == null
        ? null
        : new UpdateView(_pending.Version.ToString(3), _pending.Asset.Size, Text.CleanMultiline(_pending.Notes, 20_000), _downloaded);

    public bool IsInstalled => InstallLocation.IsInstalled;

    public void StartBackgroundChecks()
    {
        _loop?.Cancel();
        _loop = new CancellationTokenSource();
        var ct = _loop.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await CheckAsync(ct);
                    if (_pending != null && settings.Current.Updates.Auto && !_downloaded)
                        await DownloadAsync(null, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Warn("Update-Prüfung fehlgeschlagen", ex);
                }
                await Task.Delay(Interval, ct);
            }
        }, ct);
    }

    public async Task<UpdateView?> CheckAsync(CancellationToken ct = default)
    {
        if (!IsInstalled)
            return null;
        var beta = settings.Current.Updates.Channel == UpdateChannel.Beta;
        var newest = (beta
                ? await api.GetJsonAsync<List<GithubRelease>>($"{ApiBase}/releases?per_page=20", ct: ct, configure: Accept)
                : [await api.GetJsonAsync<GithubRelease>($"{ApiBase}/releases/latest", ct: ct, configure: Accept)])
            .Select(ToRelease)
            .OfType<Release>()
            .MaxBy(r => r.Version);

        var current = ParseVersion(AppInfo.Version) ?? new Version(0, 0, 0);
        var info = newest != null && newest.Version > current ? newest : null;
        var isNew = info != null && info.Version != _pending?.Version;
        _pending = info;
        if (info == null)
            return null;
        if (isNew)
        {
            _downloaded = false;
            events.Publish("update.available", Available);
            events.Publish("notify", new
            {
                type = "launcherUpdate",
                text = TextRef.Of("notifications.launcherUpdate", ("version", info.Version.ToString(3)))
            });
        }
        return Available;
    }

    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (_pending == null)
            return;
        var asset = _pending.Asset;
        await downloads.DownloadAsync(new DownloadRequest(asset.BrowserDownloadUrl, DownloadPath, Size: asset.Size, Sha256: asset.Sha256),
            progress, ct);
        _downloaded = true;
        events.Publish("update.available", Available);
    }

    /// <summary>Installieren und neu starten. Läuft etwas, wird nicht installiert.</summary>
    public async Task ApplyAndRestartAsync(JobContext job)
    {
        if (busy())
            throw new AxoException("updates.busy");
        if (_pending == null)
            return;
        if (!_downloaded)
            await DownloadAsync(job.Range(0, 0.95), job.Token);
        InstallLocation.ReplaceExe(DownloadPath, copy: false);
        // Die neue Version wartet, bis diese beendet ist (sonst hält sie nur das alte Fenster nach vorn)
        Process.Start(new ProcessStartInfo(InstallLocation.ExePath) { UseShellExecute = false, ArgumentList = { "--after", Environment.ProcessId.ToString() } });
        Environment.Exit(0);
    }

    /// <summary>Beim Beenden: heruntergeladenes Update einsetzen, der nächste Start ist dann die neue Version (Automatik).</summary>
    public void ApplyOnExitIfReady()
    {
        if (_pending == null || !_downloaded || !settings.Current.Updates.Auto || !File.Exists(DownloadPath))
            return;
        try
        {
            InstallLocation.ReplaceExe(DownloadPath, copy: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Update beim Beenden einsetzen", ex);
        }
    }

    private static void Accept(HttpRequestMessage request) => request.Headers.Accept.ParseAdd("application/vnd.github+json");

    private static Release? ToRelease(GithubRelease r)
    {
        if (r.Draft || ParseVersion(r.TagName) is not { } version)
            return null;
        var asset = r.Assets?.FirstOrDefault(a => a.Name == AssetName);
        if (asset == null || !asset.BrowserDownloadUrl.StartsWith("https://", StringComparison.Ordinal))
            return null;
        return new Release(version, r.Body, asset);
    }

    /// <summary>"v1.2.3", "1.2.3-dev" → 1.2.3.</summary>
    internal static Version? ParseVersion(string? text)
    {
        var core = (text ?? "").TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }

    private sealed record Release(Version Version, string? Notes, GithubAsset Asset);

    private sealed record GithubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("assets")] List<GithubAsset>? Assets);

    private sealed record GithubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest)
    {
        /// <summary>GitHub liefert "sha256:…"; ohne Prüfsumme wird nur über HTTPS geladen.</summary>
        public string? Sha256 => Digest?.StartsWith("sha256:", StringComparison.Ordinal) == true ? Digest[7..] : null;
    }
}
