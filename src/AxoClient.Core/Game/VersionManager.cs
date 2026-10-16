using System.Text.Json.Serialization;
using AxoClient.Core.Common;

namespace AxoClient.Core.Game;

/// <summary>Minecraft-Versionen aus Mojangs <c>piston-meta</c>, eine Stunde zwischengespeichert.</summary>
public sealed class VersionManager(ApiClient api)
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private sealed record Manifest(ManifestLatest Latest, List<ManifestVersion> Versions);

    private sealed record ManifestLatest(string Release, string Snapshot);

    private sealed record ManifestVersion(
        string Id,
        string Type,
        string Url,
        [property: JsonPropertyName("releaseTime")] DateTimeOffset ReleaseTime,
        string? Sha1);

    public async Task<IReadOnlyList<GameVersionInfo>> ListAsync(CancellationToken ct = default)
    {
        var manifest = await api.GetJsonAsync<Manifest>(ManifestUrl, TimeSpan.FromHours(1), ct);
        return manifest.Versions.Select(v => new GameVersionInfo(v.Id, v.Type switch
        {
            "release" => VersionType.Release,
            "snapshot" => VersionType.Snapshot,
            _ => VersionType.Old
        }, v.ReleaseTime)).ToList();
    }

    public async Task<string> LatestReleaseAsync(CancellationToken ct = default) =>
        (await api.GetJsonAsync<Manifest>(ManifestUrl, TimeSpan.FromHours(1), ct)).Latest.Release;

    public async Task<VersionType?> TypeOfAsync(string version, CancellationToken ct = default) =>
        (await ListAsync(ct)).FirstOrDefault(v => v.Id == version)?.Type;

    /// <summary>"1.21.4" → (1, 21, 4); "26.1" → (26, 1, 0). Snapshots liefern <c>null</c>.</summary>
    public static (int Major, int Minor, int Patch)? Parse(string version)
    {
        var parts = version.Split('.');
        if (parts.Length is < 2 or > 3 || !parts.All(p => p.All(char.IsAsciiDigit) && p.Length > 0))
            return null;
        return (int.Parse(parts[0]), int.Parse(parts[1]), parts.Length == 3 ? int.Parse(parts[2]) : 0);
    }

    public static int Compare(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        if (pa == null || pb == null)
            return string.CompareOrdinal(a, b);
        return pa.Value.CompareTo(pb.Value);
    }
}
