using AxoClient.Common;
using AxoClient.DataModel.Instances;
using AxoClient.DataModel.Launch;
using AxoClient.Instances.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>RAM: mehr als der PC hat → Blocker; mehr als gerade frei oder zu wenig für die Mods → Warnung.</summary>
    public sealed class MemoryCheck : LaunchCheck
    {
        private readonly IInstanceManager _instances;
        private readonly ISystemInfo _systemInfo;
        private readonly Func<(long TotalMb, long FreeMb)>? _memory;

        public MemoryCheck(IInstanceManager instances, ISystemInfo systemInfo, Func<(long TotalMb, long FreeMb)>? memory = null)
        {
            _instances = instances;
            _systemInfo = systemInfo;
            _memory = memory;
        }

        public override string Id => "memory";
        public override LaunchPhase Phase => LaunchPhase.Resources;

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken)
        {
            var (total, free) = (_memory ?? _systemInfo.Memory)();
            var memoryMb = context.MemoryMb;
            var mods = context.Content.Mods.Count(mod => mod.Enabled);
            var recommended = _systemInfo.RecommendedMemoryMb(mods, total);
            var setRecommended = new CheckAction("setRecommended", new TextRef("pipeline.action.setRam", ("mb", recommended)),
                async (launchContext, job) =>
                {
                    launchContext.Instance = await _instances.Update(launchContext.Instance.Id, new InstanceUpdate(MemoryMb: recommended), job.Token);
                }, true);

            var results = new List<CheckResult>();
            if (memoryMb > total - 512)
            {
                results.Add(new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.ram.tooHigh", ("mb", memoryMb), ("totalMb", total)),
                    [setRecommended], CanSilence: false));
            }
            else
            {
                if (memoryMb > free)
                {
                    results.Add(new CheckResult(Id, Severity.Warning, new TextRef("pipeline.ram.notFree", ("mb", memoryMb), ("freeMb", free)),
                        [
                            new CheckAction("lessRam", new TextRef("pipeline.action.lessRam", ("mb", Math.Max(1024, free / 512 * 512))),
                                async (launchContext, job) => launchContext.Instance = await _instances.Update(launchContext.Instance.Id,
                                    new InstanceUpdate(MemoryMb: (int)Math.Max(1024, free / 512 * 512)), job.Token))
                        ], Key: "ram-not-free"));
                }

                if (memoryMb < _systemInfo.MinimumMemoryMb(mods))
                {
                    results.Add(new CheckResult(Id, Severity.Warning, new TextRef("pipeline.ram.tooLow", ("mb", memoryMb), ("mods", mods)),
                        [setRecommended], Key: $"ram-low:{mods / 25}"));
                }
            }
            return Task.FromResult<IReadOnlyList<CheckResult>>(results.Count == 0 ? [Ok()] : results);
        }
    }
}
