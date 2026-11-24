using System.Text.Json;
using AxoClient.Common;
using AxoClient.DataModel.Servers;
using AxoClient.Game.Contract;

namespace AxoClient.Servers
{
    /// <summary>Paper und Velocity über die Fill-API (v3) von PaperMC.</summary>
    public abstract class PaperProjectServer : ServerSoftwareBase
    {
        private readonly IApiClient _api;
        private readonly string _project;
        private const string Fill = "https://fill.papermc.io/v3/projects/";

        protected PaperProjectServer(IApiClient api, string project, IGameVersionHelper gameVersionHelper)
            : base(gameVersionHelper)
        {
            _api = api;
            _project = project;
        }

        public override async Task<IReadOnlyList<ServerVersionInfo>> ListVersions(CancellationToken ct)
        {
            using var json = JsonDocument.Parse(await _api.GetString(Fill + _project, TimeSpan.FromHours(1), ct));
            var versions = json.RootElement.GetProperty("versions");
            var ids = versions.ValueKind == JsonValueKind.Object
                ? versions.EnumerateObject().SelectMany(g => g.Value.EnumerateArray().Select(v => v.GetString()!)).ToList()
                : versions.EnumerateArray().Select(v => v.GetString()!).Reverse().ToList();
            return ids.Select(id => new ServerVersionInfo(id, Type == ServerSoftware.Velocity
                                                              || !(id.Contains("-pre", StringComparison.OrdinalIgnoreCase)
                                                                   || id.Contains("-rc", StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }

        public override async Task<DownloadRequest> ResolveJar(string version, string targetPath, CancellationToken ct)
        {
            using var build = JsonDocument.Parse(await _api.GetString(
                $"{Fill}{_project}/versions/{Uri.EscapeDataString(version)}/builds/latest", ct: ct));
            var downloads = build.RootElement.GetProperty("downloads");
            var server = downloads.TryGetProperty("server:default", out var main) ? main : downloads.EnumerateObject().First().Value;
            var checksums = server.TryGetProperty("checksums", out var c) ? c : default;
            return new DownloadRequest(server.GetProperty("url").GetString()!, targetPath,
                Sha256: checksums.ValueKind == JsonValueKind.Object && checksums.TryGetProperty("sha256", out var sha) ? sha.GetString() : null,
                Size: server.TryGetProperty("size", out var size) ? size.GetInt64() : null);
        }
    }
}
