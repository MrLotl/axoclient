using AxoClient.Core.Accounts;
using AxoClient.Core.Common;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Launch.Checks;

/// <summary>Konto: Token abgelaufen → still erneuern; schlägt das fehl → Blocker "Neu anmelden".</summary>
public sealed class AccountCheck(AccountManager accounts) : ILaunchCheck
{
    public string Id => "account";
    public LaunchPhase Phase => LaunchPhase.Environment;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        if (accounts.Active == null)
            return [new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.account.none"),
                [new CheckAction("login", new TextRef("pipeline.action.login"), Login, true)], CanSilence: false)];
        try
        {
            ctx.Session = await accounts.GetSessionAsync(ct);
            return [CheckResult.Ok(Id)];
        }
        catch (AxoException ex) when (ex.Code == "errors.offline")
        {
            // Offline: Microsoft-Spiele starten trotzdem (nur Einzelspieler sinnvoll)
            ctx.Session = accounts.CachedSession;
            ctx.Offline = true;
            return ctx.Session != null
                ? [CheckResult.Ok(Id)]
                : [new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.account.offlineNoSession"),
                    [new CheckAction("retry", new TextRef("pipeline.action.retry"), (_, _) => Task.CompletedTask, true)], CanSilence: false)];
        }
        catch (AxoException ex) when (ex.Code == "auth.expired")
        {
            return [new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.account.expired"),
                [new CheckAction("login", new TextRef("pipeline.action.login"), Login, true)], CanSilence: false)];
        }
    }

    private async Task Login(LaunchContext ctx, JobContext job)
    {
        var type = accounts.Active?.Type ?? AccountType.Microsoft;
        await accounts.LoginAsync(type, job.Token);
        ctx.Session = await accounts.GetSessionAsync(job.Token);
    }
}

/// <summary>Internet: offline mit allen Dateien → offline starten; offline ohne Dateien → Blocker.</summary>
public sealed class InternetCheck(ApiClient api, Game.GameInstaller installer) : ILaunchCheck
{
    public string Id => "internet";
    public LaunchPhase Phase => LaunchPhase.Environment;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        if (await IsOnlineAsync(api, ct))
            return [CheckResult.Ok(Id)];
        ctx.Offline = true;
        var installed = installer.InstalledVersionName(ctx.Instance.Minecraft.Version, ctx.Instance.Loader.Type,
            ctx.Instance.Loader.Version);
        if (installed != null && installer.IsVersionPresent(installed))
        {
            ctx.VersionName = installed;
            return [CheckResult.Ok(Id)];
        }
        return [new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.internet.missingFiles"),
            [new CheckAction("retry", new TextRef("pipeline.action.retry"), (c, _) =>
            {
                c.Offline = false;
                return Task.CompletedTask;
            }, true)], CanSilence: false)];
    }

    public static async Task<bool> IsOnlineAsync(ApiClient api, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var response = await api.Http.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"), timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }
}

/// <summary>Speicherplatz: zu wenig für die nötigen Downloads → Blocker mit nötiger Größe.</summary>
public sealed class DiskSpaceCheck(AppPaths paths, Game.GameInstaller installer) : ILaunchCheck
{
    private const long NewVersionBytes = 700L * 1024 * 1024;
    private const long Reserve = 200L * 1024 * 1024;

    public string Id => "disk";
    public LaunchPhase Phase => LaunchPhase.Environment;

    public Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        var installed = installer.InstalledVersionName(ctx.Instance.Minecraft.Version, ctx.Instance.Loader.Type, ctx.Instance.Loader.Version);
        var needed = (installed == null ? NewVersionBytes : 0) + Reserve;
        var free = SystemInfo.FreeDiskBytes(paths.Root);
        IReadOnlyList<CheckResult> result = free >= needed
            ? [CheckResult.Ok(Id)]
            : [new CheckResult(Id, Severity.Blocker, TextRef.Of("pipeline.disk.tooLow",
                    ("needMb", needed / 1024 / 1024), ("freeMb", free / 1024 / 1024)),
                [new CheckAction("retry", new TextRef("pipeline.action.retry"), (_, _) => Task.CompletedTask, true)], CanSilence: false)];
        return Task.FromResult(result);
    }
}

/// <summary>Läuft schon: Warnung (immer, nicht stummschaltbar), weil dieselbe Welt beschädigt werden kann.</summary>
public sealed class AlreadyRunningCheck(ProcessRunner processes) : ILaunchCheck
{
    public string Id => "running";
    public LaunchPhase Phase => LaunchPhase.Environment;

    public Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        IReadOnlyList<CheckResult> result = processes.Get(InstanceManager.ProcessKey(ctx.Instance.Id)) == null
            ? [CheckResult.Ok(Id)]
            : [new CheckResult(Id, Severity.Warning, new TextRef("pipeline.running.warning"),
                [new CheckAction("switch", new TextRef("pipeline.action.switchToGame"), (_, _) =>
                    throw new LaunchAbortedException("switch"), true)], CanSilence: false)];
        return Task.FromResult(result);
    }
}

/// <summary>Bricht den Start bewusst ab (z. B. "Zum laufenden Spiel wechseln").</summary>
public sealed class LaunchAbortedException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}
