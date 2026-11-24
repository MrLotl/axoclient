using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Launch;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>
    /// Version und Loader: fehlt etwas oder ist beschädigt → Auto-Fix (neu laden/installieren). CmlLib prüft dabei
    /// alle Dateien gegen ihre Hashes; vollständige Installationen sind in Sekunden durch.
    /// </summary>
    public sealed class GameFilesCheck : LaunchCheck
    {
        private readonly IMinecraftInstaller _installer;
        private readonly ILoaderManager _loaders;
        private readonly IInstanceManager _instances;

        public GameFilesCheck(IMinecraftInstaller installer, ILoaderManager loaders, IInstanceManager instances)
        {
            _installer = installer;
            _loaders = loaders;
            _instances = instances;
        }

        public override string Id => "gameFiles";
        public override LaunchPhase Phase => LaunchPhase.GameFiles;

        public override async Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken)
        {
            if (context.Offline)
            {
                return context.VersionName != null ? [Ok()] : [blocker()];
            }

            var instance = context.Instance;
            if (instance.Loader.Type != LoaderType.Vanilla && instance.Loader.Version == null)
            {
                // Loader-Version noch offen: empfohlene eintragen (eindeutig und ungefährlich)
                var version = await _loaders.ResolveVersion(instance.Loader.Type, instance.Minecraft.Version, null, cancellationToken);
                instance.Loader = instance.Loader with { Version = version };
                await _instances.Save(instance, cancellationToken);
            }

            var installed = _installer.InstalledVersionName(instance.Minecraft.Version, instance.Loader.Type, instance.Loader.Version);
            context.VersionName = installed;
            context.NeedsInstall = installed == null || !_installer.IsVersionPresent(installed);
            return [new CheckResult(Id, Severity.AutoFix,
                new TextRef(context.NeedsInstall ? "pipeline.gameFiles.install" : "pipeline.gameFiles.verify"),
                [new CheckAction("install", new TextRef("pipeline.action.install"), install, true)])];
        }

        private async Task install(LaunchContext context, JobContext job) =>
            context.VersionName = await _installer.Install(context.GamePath, context.Instance.Minecraft.Version, context.Instance.Loader.Type,
                context.Instance.Loader.Version, job);

        private CheckResult blocker() => new(Id, Severity.Blocker, new TextRef("pipeline.internet.missingFiles"),
            [new CheckAction("retry", new TextRef("pipeline.action.retry"), (context, _) =>
            {
                context.Offline = false;
                return Task.CompletedTask;
            }, true)], CanSilence: false);
    }
}
