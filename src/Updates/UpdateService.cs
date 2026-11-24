using System.Diagnostics;
using System.Text.Json.Serialization;
using AxoClient.Common;
using AxoClient.DataModel.Settings;
using AxoClient.DataModel.Updates;
using AxoClient.Settings.Contract;
using AxoClient.Updates.Contract;

namespace AxoClient.Updates
{
    /// <summary>
    /// Selbst-Update über GitHub Releases, ohne Zusatzwerkzeug: das Release enthält eine einzige
    /// <c>AxoClient-Setup.exe</c> (der Launcher selbst). Sie wird mit SHA-256-Prüfung geladen und gegen die
    /// installierte <c>AxoClient.exe</c> getauscht. Kanal <c>stable</c> = neuestes normales Release,
    /// <c>beta</c> = auch Vorabversionen. Nie während eine Instanz oder ein lokaler Server läuft.
    /// </summary>
    public sealed class UpdateService : IUpdateService
    {
        private readonly ISettingsService _settings;
        private readonly IEventHub _events;
        private readonly IApiClient _api;
        private readonly IDownloadManager _downloads;
        private readonly AppPaths _paths;
        private readonly Func<bool> _busy;
        private readonly ITextHelper _textHelper;
        private readonly IAppLog _appLog;
        private readonly IAppInfo _appInfo;
        private readonly IInstallLocation _installLocation;
        public const string AssetName = "AxoClient-Setup.exe";
        private readonly TimeSpan _interval = TimeSpan.FromHours(4);
        private readonly string _apiBase = AppInfo.Repository.Replace("https://github.com/", "https://api.github.com/repos/");

        private Release? _pending;
        private bool _downloaded;
        private CancellationTokenSource? _loop;

        public UpdateService(
            ISettingsService settings,
            IEventHub events,
            IApiClient api,
            IDownloadManager downloads,
            AppPaths paths,
            Func<bool> busy,
            ITextHelper textHelper,
            IAppLog appLog,
            IAppInfo appInfo,
            IInstallLocation installLocation)
        {
            _settings = settings;
            _events = events;
            _api = api;
            _downloads = downloads;
            _paths = paths;
            _busy = busy;
            _textHelper = textHelper;
            _appLog = appLog;
            _appInfo = appInfo;
            _installLocation = installLocation;
        }

        private string DownloadPath => Path.Combine(_paths.Updates, AssetName);

        public UpdateView? Available => _pending == null
            ? null
            : new UpdateView(_pending.Version.ToString(3), _pending.Asset.Size, _textHelper.CleanMultiline(_pending.Notes, 20_000), _downloaded);

        public bool IsInstalled => _installLocation.IsInstalled;

        public void StartBackgroundChecks()
        {
            _loop?.Cancel();
            _loop = new CancellationTokenSource();
            var cancellationToken = _loop.Token;
            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Check(cancellationToken);
                        if (_pending != null && _settings.Current.Updates.Auto && !_downloaded)
                        {
                            await Download(null, cancellationToken);
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        _appLog.Warn("Update-Prüfung fehlgeschlagen", exception);
                    }
                    await Task.Delay(_interval, cancellationToken);
                }
            }, cancellationToken);
        }

        public async Task<UpdateView?> Check(CancellationToken cancellationToken = default)
        {
            if (!IsInstalled)
            {
                return null;
            }

            var beta = _settings.Current.Updates.Channel == UpdateChannel.Beta;
            var newest = (beta
                    ? await _api.GetJson<List<GithubRelease>>($"{_apiBase}/releases?per_page=20", cancellationToken: cancellationToken, configure: accept)
                    : [await _api.GetJson<GithubRelease>($"{_apiBase}/releases/latest", cancellationToken: cancellationToken, configure: accept)])
                .Select(toRelease)
                .OfType<Release>()
                .MaxBy(release => release.Version);

            var current = ParseVersion(_appInfo.Version) ?? new Version(0, 0, 0);
            var newerRelease = newest != null && newest.Version > current ? newest : null;
            var isNew = newerRelease != null && newerRelease.Version != _pending?.Version;
            _pending = newerRelease;
            if (newerRelease == null)
            {
                return null;
            }

            if (isNew)
            {
                _downloaded = false;
                _events.Publish("update.available", Available);
                _events.Publish("notify", new
                {
                    type = "launcherUpdate",
                    text = new TextRef("notifications.launcherUpdate", ("version", newerRelease.Version.ToString(3)))
                });
            }
            return Available;
        }

        public async Task Download(IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (_pending == null)
            {
                return;
            }

            var asset = _pending.Asset;
            await _downloads.Download(new DownloadRequest(asset.BrowserDownloadUrl, DownloadPath, Size: asset.Size, Sha256: asset.Sha256),
                progress, cancellationToken);
            _downloaded = true;
            _events.Publish("update.available", Available);
        }

        /// <summary>Installieren und neu starten. Läuft etwas, wird nicht installiert.</summary>
        public async Task ApplyAndRestart(JobContext job)
        {
            if (_busy())
            {
                throw new AxoException("updates.busy");
            }

            if (_pending == null)
            {
                return;
            }

            if (!_downloaded)
            {
                await Download(job.Range(0, 0.95), job.Token);
            }

            _installLocation.ReplaceExe(DownloadPath, copy: false);
            // Die neue Version wartet, bis diese beendet ist (sonst hält sie nur das alte Fenster nach vorn)
            Process.Start(new ProcessStartInfo(_installLocation.ExePath) { UseShellExecute = false, ArgumentList = { "--after", Environment.ProcessId.ToString() } });
            Environment.Exit(0);
        }

        /// <summary>Beim Beenden: heruntergeladenes Update einsetzen, der nächste Start ist dann die neue Version (Automatik).</summary>
        public void ApplyOnExitIfReady()
        {
            if (_pending == null || !_downloaded || !_settings.Current.Updates.Auto || !File.Exists(DownloadPath))
            {
                return;
            }

            try
            {
                _installLocation.ReplaceExe(DownloadPath, copy: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _appLog.Warn("Update beim Beenden einsetzen", exception);
            }
        }

        private void accept(HttpRequestMessage request) => request.Headers.Accept.ParseAdd("application/vnd.github+json");

        private Release? toRelease(GithubRelease githubRelease)
        {
            if (githubRelease.Draft || ParseVersion(githubRelease.TagName) is not { } version)
            {
                return null;
            }

            var asset = githubRelease.Assets?.FirstOrDefault(githubAsset => githubAsset.Name == AssetName);
            if (asset == null || !asset.BrowserDownloadUrl.StartsWith("https://", StringComparison.Ordinal))
            {
                return null;
            }

            return new Release(version, githubRelease.Body, asset);
        }

        /// <summary>"v1.2.3", "1.2.3-dev" → 1.2.3.</summary>
        internal Version? ParseVersion(string? text)
        {
            var core = (text ?? "").TrimStart('v', 'V').Split('-', '+')[0];
            return Version.TryParse(core, out var parsed) ? new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build)) : null;
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
}
