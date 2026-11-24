using System.Text.Json;
using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Servers;
using AxoClient.Game.Contract;

namespace AxoClient.Servers
{
    public sealed class VanillaServer : ServerSoftwareBase
    {
        private readonly IApiClient _api;
        private readonly IVersionManager _versions;

        public VanillaServer(IApiClient api, IVersionManager versions, IGameVersionHelper gameVersionHelper)
            : base(gameVersionHelper)
        {
            _api = api;
            _versions = versions;
        }

        public override ServerSoftware Type => ServerSoftware.Vanilla;

        public override async Task<IReadOnlyList<ServerVersionInfo>> ListVersions(CancellationToken ct) =>
            (await _versions.List(ct)).Where(v => v.Type != VersionType.Old)
            .Select(v => new ServerVersionInfo(v.Id, v.Type == VersionType.Release)).ToList();

        public override async Task<DownloadRequest> ResolveJar(string version, string targetPath, CancellationToken ct)
        {
            using var manifest = JsonDocument.Parse(await _api.GetString(
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", TimeSpan.FromHours(1), ct));
            var entry = manifest.RootElement.GetProperty("versions").EnumerateArray()
                .FirstOrDefault(v => v.GetProperty("id").GetString() == version);
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new AxoException("servers.versionUnknown", new() { ["version"] = version });
            }

            using var details = JsonDocument.Parse(await _api.GetString(entry.GetProperty("url").GetString()!, TimeSpan.FromDays(7), ct));
            if (!details.RootElement.GetProperty("downloads").TryGetProperty("server", out var server))
            {
                throw new AxoException("servers.noOfficialServer", new() { ["version"] = version });
            }

            return new DownloadRequest(server.GetProperty("url").GetString()!, targetPath, server.GetProperty("sha1").GetString(),
                Size: server.GetProperty("size").GetInt64());
        }
    }
}
