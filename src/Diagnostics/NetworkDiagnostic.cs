using System.Diagnostics;
using AxoClient.Common;
using AxoClient.DataModel.Diagnostics;
using AxoClient.Diagnostics.Contract;

namespace AxoClient.Diagnostics
{
    public sealed class NetworkDiagnostic : IDiagnosticCheck
    {
        private readonly IAppInfo _appInfo;
        private readonly IApiClient _api;
        private readonly (string Name, string Url)[] _targets;

        public NetworkDiagnostic(IApiClient api, IAppInfo appInfo)
        {
            _targets =
            [
                ("Mojang", "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"),
                ("Microsoft", "https://login.live.com/"),
                ("Modrinth", "https://api.modrinth.com/v2/tag/loader"),
                ("AxoClient", appInfo.ServiceUrl + "/v2/health"),
                ("GitHub", "https://api.github.com/")
            ];
            _appInfo = appInfo;
            _targets =
            [
                ("Mojang", "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"),
                ("Microsoft", "https://login.live.com/"),
                ("Modrinth", "https://api.modrinth.com/v2/tag/loader"),
                ("AxoClient", appInfo.ServiceUrl + "/v2/health"),
                ("GitHub", "https://api.github.com/")
            ];
            _api = api;
        }

        public string Area => "network";

        public async Task<IReadOnlyList<DiagnosticItem>> Run(CancellationToken cancellationToken)
        {
            var tasks = _targets.Select(async target =>
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    using var response = await _api.Http.GetAsync(target.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    return new DiagnosticItem(Area, (int)response.StatusCode < 500 ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
                        new TextRef("diagnostics.reachable", ("name", target.Name), ("ms", watch.ElapsedMilliseconds)));
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    return new DiagnosticItem(Area, DiagnosticStatus.Error, new TextRef("diagnostics.unreachable", ("name", target.Name)),
                        Solution: new TextRef("diagnostics.unreachable.solution"));
                }
            });
            return await Task.WhenAll(tasks);
        }
    }
}
