using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Launch;

namespace AxoClient.Launch.Checks
{
    /// <summary>
    /// „Freunde in Einzelspieler-Welten nachjoinen lassen“: legt e4mc (Modrinth) in Fabric- und Quilt-Instanzen.
    /// Ist e4mc schon eingetragen – auch deaktiviert –, bleibt die Entscheidung beim Nutzer.
    /// </summary>
    public sealed class JoinRelayCheck : LaunchCheck
    {
        private readonly IContentManager _content;
        private readonly IAppLog _appLog;
        internal const string E4mcProject = "qANg5Jrr";

        public JoinRelayCheck(IContentManager content, IAppLog appLog)
        {
            _content = content;
            _appLog = appLog;
        }

        public override string Id => "joinRelay";
        public override LaunchPhase Phase => LaunchPhase.Content;

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext ctx, CancellationToken ct)
        {
            var wanted = ctx.Settings.Integrations.FriendJoinRelay
                         && ctx.Instance.Loader.Type is LoaderType.Fabric or LoaderType.Quilt
                         && !ctx.Content.Mods.Any(m => m.ProjectId == E4mcProject);
            IReadOnlyList<CheckResult> results = wanted
                ? [new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.relay.install"),
                    [new CheckAction("install", new TextRef("pipeline.action.install"), install, true)])]
                : [Ok()];
            return Task.FromResult(results);
        }

        private async Task install(LaunchContext ctx, JobContext job)
        {
            try
            {
                // e4mc gehört zur Launcher-Funktion "Nachjoinen" und ist vom Mod-Schutz ausgenommen
                await _content.Install(ctx.Instance.Id, ContentType.Mod, new InstallRequest("modrinth", E4mcProject), job, ignoreModLock: true);
                ctx.Content = await _content.Sync(ctx.Instance.Id, job.Token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Ohne e4mc startet das Spiel trotzdem – nur das Nachjoinen in Einzelspieler-Welten fehlt.
                _appLog.Warn($"e4mc konnte nicht installiert werden: {e.Message}");
                ctx.Hints.Add(new TextRef("pipeline.relay.unavailable"));
            }
        }
    }
}
