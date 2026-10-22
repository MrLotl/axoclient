using AxoClient.Core.Common;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace AxoClient.Core.Launch;

/// <summary>Erweiterungen rund um den Spielstart (AxoMod-Verbindung, Discord-Status, Online-Status …).</summary>
public interface IGameLaunchHook
{
    /// <summary>Zusätzliche JVM-Argumente für diesen Start.</summary>
    IEnumerable<string> JvmArguments(LaunchContext ctx) => [];

    void OnStarted(LaunchContext ctx, RunningProcess process)
    {
    }

    void OnExited(LaunchContext ctx, RunningProcess process, int exitCode, bool crashed)
    {
    }
}

public sealed record RunningGame(string InstanceId, string Name, DateTimeOffset StartedAt, int Pid);

/// <summary>Ein Absturz mit Analyse für den Absturz-Dialog.</summary>
public sealed record CrashEvent(string InstanceId, string InstanceName, CrashAnalysis Analysis);

/// <summary>Startet Instanzen: Pipeline, Prozess bauen, Spielzeit mitschreiben, Abstürze erkennen.</summary>
public sealed class GameLauncher(
    LaunchPipeline pipeline,
    GameInstaller installer,
    ProcessRunner processes,
    JobManager jobs,
    InstanceManager instances,
    ActivityTracker activity,
    CrashAnalyzer crashAnalyzer,
    AppPaths paths,
    EventHub events,
    IEnumerable<IGameLaunchHook> hooks)
{
    private readonly IReadOnlyList<IGameLaunchHook> _hooks = hooks.ToList();
    private readonly Dictionary<string, LaunchContext> _contexts = new();

    public event Action<RunningGame>? GameStarted;
    public event Action<string>? GameExited;

    public IReadOnlyList<RunningGame> Running => processes.All
        .Where(p => !p.HasExited && p.Key.StartsWith("instance:"))
        .Select(p =>
        {
            var id = p.Key["instance:".Length..];
            lock (_contexts)
                return new RunningGame(id, _contexts.TryGetValue(id, out var c) ? c.Instance.Name : id, p.StartedAt, p.Pid);
        }).ToList();

    public bool AnyRunning => processes.AnyRunning(k => k.StartsWith("instance:"));

    public bool IsRunning(string instanceId) => processes.Get(InstanceManager.ProcessKey(instanceId)) != null;

    /// <summary>Startet eine Instanz als Job. Fortschritt und Dialoge laufen über die Bridge.</summary>
    public Task LaunchAsync(string instanceId, QuickPlay? quickPlay = null)
    {
        if (jobs.IsRunning("launch", instanceId))
            throw new AxoException("launch.alreadyStarting");
        return jobs.RunAsync("launch", new TextRef("launch.title"), async job =>
        {
            var ctx = await pipeline.PrepareAsync(instanceId, quickPlay, job);
            job.Status("launch.starting");
            var process = await BuildProcessAsync(ctx, job.Token);
            lock (_contexts)
                _contexts[instanceId] = ctx;
            var running = processes.Start(InstanceManager.ProcessKey(instanceId), process.StartInfo);
            var key = running.Key;
            running.LineReceived += line => events.Publish("console.line", new { key, line });
            foreach (var hint in ctx.Hints)
                events.Publish("notify", new { type = "hint", text = hint, instanceId });
            foreach (var hook in _hooks)
                SafeHook(() => hook.OnStarted(ctx, running));
            GameStarted?.Invoke(new RunningGame(instanceId, ctx.Instance.Name, running.StartedAt, running.Pid));
            events.Publish("game.started", new { instanceId });
            _ = WatchAsync(ctx, running);
        }, instanceId);
    }

    public async Task StopAsync(string instanceId)
    {
        if (processes.Get(InstanceManager.ProcessKey(instanceId)) is { } running)
            await running.StopAsync();
    }

    private async Task<System.Diagnostics.Process> BuildProcessAsync(LaunchContext ctx, CancellationToken ct)
    {
        var session = ctx.Session ?? throw new AxoException("auth.noAccount");
        var launcher = installer.CreateLauncher(ctx.GamePath);
        var option = new MLaunchOption
        {
            Session = new MSession(session.Name, session.AccessToken, session.Uuid.Replace("-", ""))
            {
                Xuid = session.Xuid,
                UserType = session.Type == Accounts.AccountType.Dev ? "legacy" : "msa"
            },
            MaximumRamMb = ctx.MemoryMb,
            MinimumRamMb = Math.Min(1024, ctx.MemoryMb),
            GameLauncherName = AppInfo.Name,
            GameLauncherVersion = AppInfo.Version,
            FullScreen = ctx.Settings.Game.WindowMode == WindowMode.Fullscreen
        };
        if (ctx.Settings.Game.WindowMode == WindowMode.Window)
        {
            option.ScreenWidth = ctx.Settings.Game.WindowSize.W;
            option.ScreenHeight = ctx.Settings.Game.WindowSize.H;
        }
        if (ctx.JavaOverride is { } java && File.Exists(java))
            option.JavaPath = java;

        if (ctx.QuickPlay?.Server is { } server && ServerPing.IsValidAddress(server))
        {
            var (host, port) = ServerPing.ParseAddress(server);
            option.ServerIp = host;
            option.ServerPort = port;
        }
        else if (ctx.QuickPlay?.World is { } world && PathGuard.IsSafeFileName(world))
            option.QuickPlaySingleplayer = world;

        var jvm = new List<MArgument>();
        if (!string.IsNullOrWhiteSpace(ctx.Instance.JavaArgs))
            jvm.AddRange(Checks.JavaArgsCheck.Split(ctx.Instance.JavaArgs).Select(a => new MArgument(a)));
        foreach (var hook in _hooks)
            jvm.AddRange(hook.JvmArguments(ctx).Select(a => new MArgument(a)));
        if (jvm.Count > 0)
            option.ExtraJvmArguments = jvm;

        return await launcher.BuildProcessAsync(ctx.VersionName!, option, ct);
    }

    private async Task WatchAsync(LaunchContext ctx, RunningProcess running)
    {
        var exitCode = await running.Exited;
        var end = DateTimeOffset.Now;
        try
        {
            await activity.AddSessionAsync(paths.PlaytimeJson(ctx.Instance.Id), ActivityTracker.Kind.Playtime, running.StartedAt, end);
        }
        catch (Exception ex) when (ex is IOException or AxoException)
        {
            AppLog.Warn("Spielzeit konnte nicht gespeichert werden", ex);
        }

        var (crashText, crashFile, hsErr) = CrashAnalyzer.CollectFiles(ctx.GameDir, running.StartedAt);
        var crashed = !running.StopRequested && (exitCode != 0 || crashText != null || hsErr != null)
                      // Minecraft beendet sich bei manchen Fehlern mit 0, schreibt aber einen Crash-Report
                      && (end - running.StartedAt).TotalSeconds > 1;
        foreach (var hook in _hooks)
            SafeHook(() => hook.OnExited(ctx, running, exitCode, crashed));
        GameExited?.Invoke(ctx.Instance.Id);
        events.Publish("game.exited", new { instanceId = ctx.Instance.Id, exitCode, crashed });
        events.Publish("instances.changed");

        if (!crashed)
            return;
        var content = await instances.ContentOf(ctx.Instance.Id).LoadAsync();
        var analysis = crashAnalyzer.Analyze(new CrashInput(exitCode, crashText, hsErr, running.Tail(), crashFile), content);
        AppLog.Info($"Absturz erkannt ({ctx.Instance.Id}, Exit {exitCode}): {analysis.RuleId ?? "unbekannt"}");
        events.Publish("game.crashed", new CrashEvent(ctx.Instance.Id, ctx.Instance.Name, analysis));
    }

    /// <summary>Letzte Log-Zeilen einer Instanz (laufend oder gerade beendet), für Konsole und Absturz-Dialog.</summary>
    public IReadOnlyList<LogLine> LogOf(string instanceId, int max = 5000) =>
        processes.GetIncludingExited(InstanceManager.ProcessKey(instanceId))?.Lines(max) ?? [];

    private static void SafeHook(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLog.Error("Start-Erweiterung fehlgeschlagen", ex);
        }
    }
}
