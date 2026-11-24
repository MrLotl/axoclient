using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.Instances.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>Läuft schon: Warnung (immer, nicht stummschaltbar), weil dieselbe Welt beschädigt werden kann.</summary>
    public sealed class AlreadyRunningCheck : LaunchCheck
    {
        private readonly IProcessRunner _processes;
        private readonly IInstanceManager _instances;

        public AlreadyRunningCheck(IProcessRunner processes, IInstanceManager instances)
        {
            _processes = processes;
            _instances = instances;
        }

        public override string Id => "running";
        public override LaunchPhase Phase => LaunchPhase.Environment;

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken)
        {
            IReadOnlyList<CheckResult> result = _processes.Get(_instances.ProcessKey(context.Instance.Id)) == null
                ? [Ok()]
                : [new CheckResult(Id, Severity.Warning, new TextRef("pipeline.running.warning"),
                    [new CheckAction("switch", new TextRef("pipeline.action.switchToGame"), (_, _) =>
                        throw new LaunchAbortedException("switch"), true)], CanSilence: false)];
            return Task.FromResult(result);
        }
    }
}
