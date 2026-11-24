using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.Game.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>Speicherplatz: zu wenig für die nötigen Downloads → Blocker mit nötiger Größe.</summary>
    public sealed class DiskSpaceCheck : LaunchCheck
    {
        private readonly AppPaths _paths;
        private readonly IMinecraftInstaller _installer;
        private readonly ISystemInfo _systemInfo;
        private const long NewVersionBytes = 700L * 1024 * 1024;
        private const long Reserve = 200L * 1024 * 1024;

        public DiskSpaceCheck(AppPaths paths, IMinecraftInstaller installer, ISystemInfo systemInfo)
        {
            _paths = paths;
            _installer = installer;
            _systemInfo = systemInfo;
        }

        public override string Id => "disk";
        public override LaunchPhase Phase => LaunchPhase.Environment;

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext ctx, CancellationToken ct)
        {
            var installed = _installer.InstalledVersionName(ctx.Instance.Minecraft.Version, ctx.Instance.Loader.Type, ctx.Instance.Loader.Version);
            var needed = (installed == null ? NewVersionBytes : 0) + Reserve;
            var free = _systemInfo.FreeDiskBytes(_paths.Root);
            IReadOnlyList<CheckResult> result = free >= needed
                ? [Ok()]
                : [new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.disk.tooLow",
                        ("needMb", needed / 1024 / 1024), ("freeMb", free / 1024 / 1024)),
                    [new CheckAction("retry", new TextRef("pipeline.action.retry"), (_, _) => Task.CompletedTask, true)], CanSilence: false)];
            return Task.FromResult(result);
        }
    }
}
