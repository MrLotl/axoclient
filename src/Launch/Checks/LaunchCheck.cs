using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Launch;
using AxoClient.Launch.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>Basis aller Prüfungen vor dem Start: gemeinsame Ergebnisse und Aktionen.</summary>
    public abstract class LaunchCheck : ILaunchCheck
    {
        public abstract string Id { get; }
        public abstract LaunchPhase Phase { get; }

        public abstract Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken);

        /// <summary>Alles in Ordnung.</summary>
        protected CheckResult Ok()
        {
            return new CheckResult(Id, Severity.Ok, new TextRef("pipeline.ok"), []);
        }

        /// <summary>Aktion "Mod deaktivieren" für eine Datei der Instanz.</summary>
        protected CheckAction DisableAction(IContentManager content, ContentType type, string file)
        {
            return new CheckAction("disable", new TextRef("pipeline.action.disableMod"),
                (context, job) => content.SetEnabled(context.Instance.Id, type, file, false, job.Token));
        }
    }
}
