using AxoClient.Common;
using AxoClient.DataModel.Accounts;
using AxoClient.DataModel.Launch;
using AxoClient.DataModel.Settings;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace AxoClient.Launch
{
    /// <summary>Startet Instanzen: Pipeline, Prozess bauen, Spielzeit mitschreiben, Abstürze erkennen.</summary>
    public sealed class GameLauncher : IGameLauncher
    {
        private readonly ILaunchPipeline _pipeline;
        private readonly IMinecraftInstaller _installer;
        private readonly IProcessRunner _processes;
        private readonly IJobManager _jobs;
        private readonly IInstanceManager _instances;
        private readonly IActivityTracker _activity;
        private readonly ICrashAnalyzer _crashAnalyzer;
        private readonly AppPaths _paths;
        private readonly IEventHub _events;
        private readonly IPathGuard _pathGuard;
        private readonly IAppLog _appLog;
        private readonly IAppInfo _appInfo;
        private readonly IServerPing _serverPing;
        private readonly IJavaArgumentsHelper _javaArgumentsHelper;
        private readonly IReadOnlyList<IGameLaunchHook> _hooks;
        private readonly Dictionary<string, LaunchContext> _contexts = new();

        public event Action<RunningGame>? GameStarted;
        public event Action<string>? GameExited;

        public GameLauncher(
            ILaunchPipeline pipeline,
            IMinecraftInstaller installer,
            IProcessRunner processes,
            IJobManager jobs,
            IInstanceManager instances,
            IActivityTracker activity,
            ICrashAnalyzer crashAnalyzer,
            AppPaths paths,
            IEventHub events,
            IEnumerable<IGameLaunchHook> hooks,
            IPathGuard pathGuard,
            IAppLog appLog,
            IAppInfo appInfo,
            IServerPing serverPing,
            IJavaArgumentsHelper javaArgumentsHelper)
        {
            _pipeline = pipeline;
            _installer = installer;
            _processes = processes;
            _jobs = jobs;
            _instances = instances;
            _activity = activity;
            _crashAnalyzer = crashAnalyzer;
            _paths = paths;
            _events = events;
            _pathGuard = pathGuard;
            _appLog = appLog;
            _appInfo = appInfo;
            _serverPing = serverPing;
            _javaArgumentsHelper = javaArgumentsHelper;
            _hooks = hooks.ToList();
        }

        public IReadOnlyList<RunningGame> Running => _processes.All
            .Where(p => !p.HasExited && p.Key.StartsWith("instance:"))
            .Select(p =>
            {
                var id = p.Key["instance:".Length..];
                lock (_contexts)
                {
                    return new RunningGame(id, _contexts.TryGetValue(id, out var c) ? c.Instance.Name : id, p.StartedAt, p.Pid);
                }
            }).ToList();

        public bool AnyRunning => _processes.AnyRunning(k => k.StartsWith("instance:"));

        public bool IsRunning(string instanceId) => _processes.Get(_instances.ProcessKey(instanceId)) != null;

        /// <summary>Startet eine Instanz als Job. Fortschritt und Dialoge laufen über die Bridge.</summary>
        public Task Launch(string instanceId, QuickPlay? quickPlay = null)
        {
            if (_jobs.IsRunning("launch", instanceId))
            {
                throw new AxoException("launch.alreadyStarting");
            }

            return _jobs.Run("launch", new TextRef("launch.title"), async job =>
            {
                var ctx = await _pipeline.Prepare(instanceId, quickPlay, job);
                job.Status("launch.starting");
                var process = await buildProcess(ctx, job.Token);
                lock (_contexts)
                {
                    _contexts[instanceId] = ctx;
                }

                var running = _processes.Start(_instances.ProcessKey(instanceId), process.StartInfo);
                var key = running.Key;
                running.LineReceived += line => _events.Publish("console.line", new { key, line });
                foreach (var hint in ctx.Hints)
                {
                    _events.Publish("notify", new { type = "hint", text = hint, instanceId });
                }

                foreach (var hook in _hooks)
                {
                    safeHook(() => hook.OnStarted(ctx, running));
                }

                GameStarted?.Invoke(new RunningGame(instanceId, ctx.Instance.Name, running.StartedAt, running.Pid));
                _events.Publish("game.started", new { instanceId });
                _ = watch(ctx, running);
            }, instanceId);
        }

        public async Task Stop(string instanceId)
        {
            if (_processes.Get(_instances.ProcessKey(instanceId)) is { } running)
            {
                await running.Stop();
            }
        }

        private async Task<System.Diagnostics.Process> buildProcess(LaunchContext ctx, CancellationToken ct)
        {
            var session = ctx.Session ?? throw new AxoException("auth.noAccount");
            var launcher = _installer.CreateLauncher(ctx.GamePath);
            var option = new MLaunchOption
            {
                Session = new MSession(session.Name, session.AccessToken, session.Uuid.Replace("-", ""))
                {
                    Xuid = session.Xuid,
                    UserType = session.Type == AccountType.Dev ? "legacy" : "msa"
                },
                MaximumRamMb = ctx.MemoryMb,
                MinimumRamMb = Math.Min(1024, ctx.MemoryMb),
                GameLauncherName = AppInfo.Name,
                GameLauncherVersion = _appInfo.Version,
                FullScreen = ctx.Settings.Game.WindowMode == WindowMode.Fullscreen
            };
            if (ctx.Settings.Game.WindowMode == WindowMode.Window)
            {
                option.ScreenWidth = ctx.Settings.Game.WindowSize.W;
                option.ScreenHeight = ctx.Settings.Game.WindowSize.H;
            }
            if (ctx.JavaOverride is { } java && File.Exists(java))
            {
                option.JavaPath = java;
            }

            if (ctx.QuickPlay?.Server is { } server && _serverPing.IsValidAddress(server))
            {
                var (host, port) = _serverPing.ParseAddress(server);
                option.ServerIp = host;
                option.ServerPort = port;
            }
            else if (ctx.QuickPlay?.World is { } world && _pathGuard.IsSafeFileName(world))
            {
                option.QuickPlaySingleplayer = world;
            }

            var jvm = new List<MArgument>();
            if (!string.IsNullOrWhiteSpace(ctx.Instance.JavaArgs))
            {
                jvm.AddRange(_javaArgumentsHelper.Split(ctx.Instance.JavaArgs).Select(a => new MArgument(a)));
            }

            foreach (var hook in _hooks)
            {
                jvm.AddRange(hook.JvmArguments(ctx).Select(a => new MArgument(a)));
            }

            if (jvm.Count > 0)
            {
                option.ExtraJvmArguments = jvm;
            }

            return await launcher.BuildProcessAsync(ctx.VersionName!, option, ct);
        }

        private async Task watch(LaunchContext ctx, RunningProcess running)
        {
            var exitCode = await running.Exited;
            var end = DateTimeOffset.Now;
            try
            {
                await _activity.AddSession(_paths.PlaytimeJson(ctx.Instance.Id), ActivitySessionKind.Playtime, running.StartedAt, end);
            }
            catch (Exception ex) when (ex is IOException or AxoException)
            {
                _appLog.Warn("Spielzeit konnte nicht gespeichert werden", ex);
            }

            var (crashText, crashFile, hsErr) = _crashAnalyzer.CollectFiles(ctx.GameDir, running.StartedAt);
            var crashed = !running.StopRequested && (exitCode != 0 || crashText != null || hsErr != null)
                          // Minecraft beendet sich bei manchen Fehlern mit 0, schreibt aber einen Crash-Report
                          && (end - running.StartedAt).TotalSeconds > 1;
            foreach (var hook in _hooks)
            {
                safeHook(() => hook.OnExited(ctx, running, exitCode, crashed));
            }

            GameExited?.Invoke(ctx.Instance.Id);
            _events.Publish("game.exited", new { instanceId = ctx.Instance.Id, exitCode, crashed });
            _events.Publish("instances.changed");

            if (!crashed)
            {
                return;
            }

            var content = await _instances.ContentOf(ctx.Instance.Id).Load();
            var analysis = _crashAnalyzer.Analyze(new CrashInput(exitCode, crashText, hsErr, running.Tail(), crashFile), content);
            _appLog.Info($"Absturz erkannt ({ctx.Instance.Id}, Exit {exitCode}): {analysis.RuleId ?? "unbekannt"}");
            _events.Publish("game.crashed", new CrashEvent(ctx.Instance.Id, ctx.Instance.Name, analysis));
        }

        /// <summary>Letzte Log-Zeilen einer Instanz (laufend oder gerade beendet), für Konsole und Absturz-Dialog.</summary>
        public IReadOnlyList<LogLine> LogOf(string instanceId, int max = 5000) =>
            _processes.GetIncludingExited(_instances.ProcessKey(instanceId))?.Lines(max) ?? [];

        private void safeHook(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _appLog.Error("Start-Erweiterung fehlgeschlagen", ex);
            }
        }
    }
}
