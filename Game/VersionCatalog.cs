using System.Text.Json;

namespace AxoClient.Game;

public static class VersionCatalog
{
    private const string MojangManifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string FabricGameVersions = "https://meta.fabricmc.net/v2/versions/game";
    private const string ForgePromotions = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";

    private record MojangVersion(string Id, string Type);

    private static List<MojangVersion>? _mojang;
    private static List<(string Version, bool Stable)>? _fabric;
    private static HashSet<string>? _forge;

    public static async Task<List<string>> GetVersionsAsync(HttpClient http, LoaderType loader, bool snapshots,
        bool oldVersions)
    {
        var mojang = _mojang ??= await GetMojangAsync(http);
        switch (loader)
        {
            case LoaderType.Fabric:
                _fabric ??= await GetFabricAsync(http);
                return _fabric.Where(v => v.Stable || snapshots).Select(v => v.Version).ToList();

            case LoaderType.Forge:
                _forge ??= await GetForgeAsync(http);
                return mojang.Where(v => _forge.Contains(v.Id)).Select(v => v.Id).ToList();

            default:
                return mojang
                    .Where(v => v.Type == "release"
                                || (snapshots && v.Type == "snapshot")
                                || (oldVersions && v.Type is "old_beta" or "old_alpha"))
                    .Select(v => v.Id)
                    .ToList();
        }
    }

    public static async Task EnsureAvailableAsync(HttpClient http, LoaderType loader, string version, string consequence)
    {
        var versions = await GetVersionsAsync(http, loader, snapshots: true, oldVersions: true);
        if (!versions.Contains(version))
            throw new InvalidOperationException($"{loader} gibt es für Minecraft {version} nicht (mehr) – {consequence}.");
    }

    private static async Task<List<MojangVersion>> GetMojangAsync(HttpClient http)
    {
        using var json = JsonDocument.Parse(await http.GetStringAsync(MojangManifest));
        return json.RootElement.GetProperty("versions").EnumerateArray()
            .Select(v => new MojangVersion(v.GetProperty("id").GetString()!, v.GetProperty("type").GetString()!))
            .ToList();
    }

    private static async Task<List<(string, bool)>> GetFabricAsync(HttpClient http)
    {
        using var json = JsonDocument.Parse(await http.GetStringAsync(FabricGameVersions));
        return json.RootElement.EnumerateArray()
            .Select(v => (v.GetProperty("version").GetString()!, v.GetProperty("stable").GetBoolean()))
            .ToList();
    }

    private static async Task<HashSet<string>> GetForgeAsync(HttpClient http)
    {
        using var json = JsonDocument.Parse(await http.GetStringAsync(ForgePromotions));
        return json.RootElement.GetProperty("promos").EnumerateObject()
            .Select(p => p.Name[..p.Name.LastIndexOf('-')])
            .ToHashSet();
    }
}
