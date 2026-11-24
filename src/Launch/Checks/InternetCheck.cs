using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.Game.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>Internet: offline mit allen Dateien → offline starten; offline ohne Dateien → Blocker.</summary>
    public sealed class InternetCheck : LaunchCheck
    {
        private readonly IApiClient _api;
        private readonly IMinecraftInstaller _installer;

        public InternetCheck(IApiClient api, IMinecraftInstaller installer)
        {
            _api = api;
            _installer = installer;
        }

        public override string Id => "internet";
        public override LaunchPhase Phase => LaunchPhase.Environment;

        public override async Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken)
        {
            if (await IsOnline(_api, cancellationToken))
            {
                return [Ok()];
            }

            context.Offline = true;
            var installed = _installer.InstalledVersionName(context.Instance.Minecraft.Version, context.Instance.Loader.Type,
                context.Instance.Loader.Version);
            if (installed != null && _installer.IsVersionPresent(installed))
            {
                context.VersionName = installed;
                return [Ok()];
            }
            return [new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.internet.missingFiles"),
                [new CheckAction("retry", new TextRef("pipeline.action.retry"), (launchContext, _) =>
                {
                    launchContext.Offline = false;
                    return Task.CompletedTask;
                }, true)], CanSilence: false)];
        }

        public async Task<bool> IsOnline(IApiClient api, CancellationToken cancellationToken)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                using var response = await api.Http.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"), timeout.Token);
                return true;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }
}
