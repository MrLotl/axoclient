using System.Text.Json.Serialization;
using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;

namespace AxoClient.Game
{
    /// <summary>Minecraft-Versionen aus Mojangs <c>piston-meta</c>, eine Stunde zwischengespeichert.</summary>
    public sealed class VersionManager : IVersionManager
    {
        private readonly IApiClient _api;
        private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        public VersionManager(IApiClient api)
        {
            _api = api;
        }

        private sealed record Manifest(ManifestLatest Latest, List<ManifestVersion> Versions);

        private sealed record ManifestLatest(string Release, string Snapshot);

        private sealed record ManifestVersion(
            string Id,
            string Type,
            string Url,
            [property: JsonPropertyName("releaseTime")] DateTimeOffset ReleaseTime,
            string? Sha1);

        public async Task<IReadOnlyList<GameVersionInfo>> List(CancellationToken ct = default)
        {
            var manifest = await _api.GetJson<Manifest>(ManifestUrl, TimeSpan.FromHours(1), ct);
            return manifest.Versions.Select(v => new GameVersionInfo(v.Id, v.Type switch
            {
                "release" => VersionType.Release,
                "snapshot" => VersionType.Snapshot,
                _ => VersionType.Old
            }, v.ReleaseTime)).ToList();
        }

        public async Task<string> LatestRelease(CancellationToken ct = default) =>
            (await _api.GetJson<Manifest>(ManifestUrl, TimeSpan.FromHours(1), ct)).Latest.Release;

        /// <summary>Adresse der Versionsdatei (JSON) bei Mojang, oder <c>null</c>, wenn es die Version nicht gibt.</summary>
        public async Task<string?> UrlOf(string version, CancellationToken ct = default) =>
            (await _api.GetJson<Manifest>(ManifestUrl, TimeSpan.FromHours(1), ct)).Versions.FirstOrDefault(v => v.Id == version)?.Url;

        public async Task<VersionType?> TypeOf(string version, CancellationToken ct = default) =>
            (await List(ct)).FirstOrDefault(v => v.Id == version)?.Type;

        /// <summary>"1.21.4" → (1, 21, 4); "26.1" → (26, 1, 0). Snapshots liefern <c>null</c>.</summary>
    }
}
