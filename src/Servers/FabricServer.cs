using System.Text.Json;
using AxoClient.Common;
using AxoClient.DataModel.Servers;
using AxoClient.Game.Contract;

namespace AxoClient.Servers
{
    /// <summary>Fabric-Server über den offiziellen Server-Launcher von Fabric (eine Jar).</summary>
    public sealed class FabricServer : ServerSoftwareBase
    {
        private readonly IApiClient _api;
        private const string Meta = "https://meta.fabricmc.net/v2/versions/";

        public FabricServer(IApiClient api, IGameVersionHelper gameVersionHelper)
            : base(gameVersionHelper)
        {
            _api = api;
        }

        public override ServerSoftware Type => ServerSoftware.Fabric;

        public override async Task<IReadOnlyList<ServerVersionInfo>> ListVersions(CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await _api.GetString(Meta + "game", TimeSpan.FromHours(1), cancellationToken));
            return json.RootElement.EnumerateArray()
                .Select(versionElement => new ServerVersionInfo(versionElement.GetProperty("version").GetString()!, versionElement.GetProperty("stable").GetBoolean())).ToList();
        }

        public override async Task<DownloadRequest> ResolveJar(string version, string targetPath, CancellationToken cancellationToken)
        {
            using var loaders = JsonDocument.Parse(await _api.GetString(Meta + "loader", TimeSpan.FromHours(6), cancellationToken));
            var loader = loaders.RootElement.EnumerateArray().First(loaderElement => loaderElement.GetProperty("stable").GetBoolean()).GetProperty("version").GetString();
            using var installers = JsonDocument.Parse(await _api.GetString(Meta + "installer", TimeSpan.FromHours(6), cancellationToken));
            var installer = installers.RootElement.EnumerateArray().First(installerElement => installerElement.GetProperty("stable").GetBoolean()).GetProperty("version").GetString();
            // Fabric liefert für die Server-Jar keinen Hash; HTTPS von meta.fabricmc.net ist die Absicherung
            return new DownloadRequest($"{Meta}loader/{Uri.EscapeDataString(version)}/{loader}/{installer}/server/jar", targetPath);
        }
    }
}
