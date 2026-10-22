using AxoClient.Core.Common;
using Velopack;
using Velopack.Sources;

namespace AxoClient.Core.Updates;

public sealed record UpdateView(string Version, long SizeBytes, string? Notes, bool Downloaded);

/// <summary>
/// Selbst-Update über Velopack und GitHub Releases, Kanal <c>stable</c> oder <c>beta</c>. Nie während eine Instanz
/// oder ein lokaler Server läuft. "Künftig automatisch": im Hintergrund laden, beim nächsten Start installieren.
/// </summary>
public sealed class UpdateService(SettingsService settings, EventHub events, Func<bool> busy)
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(4);
    private UpdateInfo? _pending;
    private bool _downloaded;
    private CancellationTokenSource? _loop;

    public UpdateView? Available => _pending == null
        ? null
        : new UpdateView(_pending.TargetFullRelease.Version.ToString(), _pending.TargetFullRelease.Size,
            Text.CleanMultiline(_pending.TargetFullRelease.NotesMarkdown, 20_000), _downloaded);

    private UpdateManager CreateManager()
    {
        var beta = settings.Current.Updates.Channel == UpdateChannel.Beta;
        return new UpdateManager(new GithubSource(AppInfo.Repository, null, beta),
            new UpdateOptions { ExplicitChannel = beta ? "beta" : "stable", AllowVersionDowngrade = !beta });
    }

    public bool IsInstalled
    {
        get
        {
            try
            {
                return CreateManager().IsInstalled;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }
    }

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
        var manager = CreateManager();
        if (!manager.IsInstalled)
            return null;
        var info = await manager.CheckForUpdatesAsync();
        ct.ThrowIfCancellationRequested();
        var isNew = info != null && info.TargetFullRelease.Version.ToString() != _pending?.TargetFullRelease.Version.ToString();
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
                text = TextRef.Of("notifications.launcherUpdate", ("version", info.TargetFullRelease.Version.ToString()))
            });
        }
        return Available;
    }

    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (_pending == null)
            return;
        await CreateManager().DownloadUpdatesAsync(_pending, p => progress?.Report(p / 100.0), ct);
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
        CreateManager().ApplyUpdatesAndRestart(_pending);
    }

    /// <summary>Beim Beenden: heruntergeladenes Update beim nächsten Start installieren (Automatik).</summary>
    public void ApplyOnExitIfReady()
    {
        if (_pending == null || !_downloaded || !settings.Current.Updates.Auto)
            return;
        try
        {
            CreateManager().WaitExitThenApplyUpdates(_pending, silent: true, restart: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Warn("Update beim Beenden vorbereiten", ex);
        }
    }
}
