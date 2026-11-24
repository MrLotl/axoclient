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

        public override async Task<IReadOnlyList<ServerVersionInfo>> ListVersions(CancellationToken ct)
        {
            using var json = JsonDocument.Parse(await _api.GetString(Meta + "game", TimeSpan.FromHours(1), ct));
            return json.RootElement.EnumerateArray()
                .Select(v => new ServerVersionInfo(v.GetProperty("version").GetString()!, v.GetProperty("stable").GetBoolean())).ToList();
        }

        public override async Task<DownloadRequest> ResolveJar(string version, string targetPath, CancellationToken ct)
        {
            using var loaders = JsonDocument.Parse(await _api.GetString(Meta + "loader", TimeSpan.FromHours(6), ct));
            var loader = loaders.RootElement.EnumerateArray().First(l => l.GetProperty("stable").GetBoolean()).GetProperty("version").GetString();
            using var installers = JsonDocument.Parse(await _api.GetString(Meta + "installer", TimeSpan.FromHours(6), ct));
            var installer = installers.RootElement.EnumerateArray().First(i => i.GetProperty("stable").GetBoolean()).GetProperty("version").GetString();
            // Fabric liefert für die Server-Jar keinen Hash; HTTPS von meta.fabricmc.net ist die Absicherung
            return new DownloadRequest($"{Meta}loader/{Uri.EscapeDataString(version)}/{loader}/{installer}/server/jar", targetPath);
        }
    }
}
