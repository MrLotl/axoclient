using AxoClient.Common;
using AxoClient.DataModel.Instances;
using AxoClient.DataModel.Launch;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>Java-Argumente: doppeltes -Xmx/-Xms → Auto-Fix (RAM-Regler hat Vorrang); ungültig → Blocker.</summary>
    public sealed class JavaArgsCheck : LaunchCheck
    {
        private readonly IInstanceManager _instances;
        private readonly IJavaArgumentsHelper _javaArgumentsHelper;

        public JavaArgsCheck(IInstanceManager instances, IJavaArgumentsHelper javaArgumentsHelper)
        {
            _instances = instances;
            _javaArgumentsHelper = javaArgumentsHelper;
        }

        public override string Id => "javaArgs";
        public override LaunchPhase Phase => LaunchPhase.Resources;

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken)
        {
            var arguments = context.Instance.JavaArgs;
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return Task.FromResult<IReadOnlyList<CheckResult>>([Ok()]);
            }

            if (!_javaArgumentsHelper.IsValid(arguments))
            {
                return Task.FromResult<IReadOnlyList<CheckResult>>([new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.javaArgs.invalid"),
                    [new CheckAction("reset", new TextRef("pipeline.action.resetArgs"), async (launchContext, job) =>
                        launchContext.Instance = await _instances.Update(launchContext.Instance.Id, new InstanceUpdate(JavaArgs: ""), job.Token), true)],
                    CanSilence: false)]);
            }

            if (_javaArgumentsHelper.HasMemoryArgument(arguments))
            {
                return Task.FromResult<IReadOnlyList<CheckResult>>([new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.javaArgs.memory"),
                    [new CheckAction("strip", new TextRef("pipeline.action.preferSlider"), async (launchContext, job) =>
                        launchContext.Instance = await _instances.Update(launchContext.Instance.Id, new InstanceUpdate(JavaArgs: _javaArgumentsHelper.StripMemory(launchContext.Instance.JavaArgs)),
                            job.Token), true)])]);
            }

            return Task.FromResult<IReadOnlyList<CheckResult>>([Ok()]);
        }
    }
}
