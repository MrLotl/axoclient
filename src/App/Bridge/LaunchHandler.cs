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

        public override void Register(MessageRouter r, MainWindow window)
        {
            r.On<LaunchData>("instances.launch", async (d, _) =>
            {
                TextHelper.RequireId(d.Id);
                var quickPlay = d.World != null || d.Server != null ? new QuickPlay(d.World, d.Server) : null;
                if (d.Server != null && !_serverPing.IsValidAddress(d.Server))
                {
                    throw new AxoException("servers.addressInvalid");
                }

                await _settings.UpdateState(s => s.LastInstance = d.Id);
                return StartJob(_games.Launch(d.Id, quickPlay));
            });

            r.On<IdData>("instances.stop", async (d, _) =>
            {
                await _games.Stop(d.Id);
                return null;
            });

            r.On("games.running", () => _games.Running);

            r.On<PromptIdData>("launch.promptShown", (d, _) => Task.FromResult<object?>(_prompt.Shown(d.PromptId)));
            r.On<ResolveData>("launch.resolve", (d, _) => Task.FromResult<object?>(_prompt.Resolve(d.PromptId,
                new PromptDecision(d.Choice, d.Actions, d.Silence))));

            r.On<CrashActionData>("crash.action", async (d, _) =>
            {
                await _jobs.Run("crash.action", new TextRef("crash.fixing"), job => _crashActions.Run(d.Id, d.Action, d.File, job), d.Id);
                return null;
            });

            // ---- Live-Konsole ----
            r.On("console.sources", () => _processes.All.Select(p => new
            {
                key = p.Key,
                kind = p.Key.StartsWith("server:") ? "server" : "instance",
                id = p.Key[(p.Key.IndexOf(':') + 1)..],
                running = !p.HasExited,
                startedAt = p.StartedAt
            }).ToList());

            r.On<ConsoleData>("console.lines", (d, _) => Task.FromResult<object?>(
                _processes.GetIncludingExited(d.Key)?.Lines(Math.Clamp(d.Max, 1, 20_000)) ?? []));

            r.On<ConsoleData>("console.copy", (d, _) => Task.FromResult<object?>(_logRedactor.RedactForSharing(
                string.Join('\n', (_processes.GetIncludingExited(d.Key)?.Lines(Math.Clamp(d.Max, 1, 20_000)) ?? []).Select(l => l.Text)))));

            r.On<CommandData>("console.command", async (d, _) =>
            {
                await _localServers.SendCommand(d.Id, d.Command);
                return null;
            });
        }
    }
}
