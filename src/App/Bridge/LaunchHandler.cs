using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Servers.Contract;
using AxoClient.Settings.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich Launch.</summary>
    public class LaunchHandler : BridgeHandler
    {
        private readonly ISettingsService _settings;
        private readonly IGameLauncher _games;
        private readonly LaunchPromptBridge _prompt;
        private readonly IJobManager _jobs;
        private readonly ICrashActions _crashActions;
        private readonly IProcessRunner _processes;
        private readonly ILocalServerManager _localServers;
        private readonly IServerPing _serverPing;
        private readonly ILogRedactor _logRedactor;

        public LaunchHandler(
            ISettingsService settings,
            IGameLauncher games,
            LaunchPromptBridge prompt,
            IJobManager jobs,
            ICrashActions crashActions,
            IProcessRunner processes,
            ILocalServerManager localServers,
            IServerPing serverPing,
            ILogRedactor logRedactor,
            IAppLog appLog,
            ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _settings = settings;
            _games = games;
            _prompt = prompt;
            _jobs = jobs;
            _crashActions = crashActions;
            _processes = processes;
            _localServers = localServers;
            _serverPing = serverPing;
            _logRedactor = logRedactor;
        }

        public override void Register(MessageRouter router, MainWindow window)
        {
            router.On<LaunchData>("instances.launch", async (request, _) =>
            {
                TextHelper.RequireId(request.Id);
                var quickPlay = request.World != null || request.Server != null ? new QuickPlay(request.World, request.Server) : null;
                if (request.Server != null && !_serverPing.IsValidAddress(request.Server))
                {
                    throw new AxoException("servers.addressInvalid");
                }

                await _settings.UpdateState(state => state.LastInstance = request.Id);
                return StartJob(_games.Launch(request.Id, quickPlay));
            });

            router.On<IdData>("instances.stop", async (request, _) =>
            {
                await _games.Stop(request.Id);
                return null;
            });

            router.On("games.running", () => _games.Running);

            router.On<PromptIdData>("launch.promptShown", (request, _) => Task.FromResult<object?>(_prompt.Shown(request.PromptId)));
            router.On<ResolveData>("launch.resolve", (request, _) => Task.FromResult<object?>(_prompt.Resolve(request.PromptId,
                new PromptDecision(request.Choice, request.Actions, request.Silence))));

            router.On<CrashActionData>("crash.action", async (request, _) =>
            {
                await _jobs.Run("crash.action", new TextRef("crash.fixing"), job => _crashActions.Run(request.Id, request.Action, request.File, job), request.Id);
                return null;
            });

            // ---- Live-Konsole ----
            router.On("console.sources", () => _processes.All.Select(process => new
            {
                key = process.Key,
                kind = process.Key.StartsWith("server:") ? "server" : "instance",
                id = process.Key[(process.Key.IndexOf(':') + 1)..],
                running = !process.HasExited,
                startedAt = process.StartedAt
            }).ToList());

            router.On<ConsoleData>("console.lines", (request, _) => Task.FromResult<object?>(
                _processes.GetIncludingExited(request.Key)?.Lines(Math.Clamp(request.Max, 1, 20_000)) ?? []));

            router.On<ConsoleData>("console.copy", (request, _) => Task.FromResult<object?>(_logRedactor.RedactForSharing(
                string.Join('\n', (_processes.GetIncludingExited(request.Key)?.Lines(Math.Clamp(request.Max, 1, 20_000)) ?? []).Select(logLine => logLine.Text)))));

            router.On<CommandData>("console.command", async (request, _) =>
            {
                await _localServers.SendCommand(request.Id, request.Command);
                return null;
            });
        }
    }
}
