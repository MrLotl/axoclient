using System.Text;
using System.Text.Json;

namespace AxoClient.Accounts;

public record PlayerSkin(byte[] Png, bool Slim, byte[]? CapePng);

public static class PlayerSkins
{
    private const string SessionApi = "https://sessionserver.mojang.com/session/minecraft/profile/";
    private const string LookupApi = "https://api.mojang.com/users/profiles/minecraft/";

    private static readonly Dictionary<string, PlayerSkin?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<PlayerSkin?> LoadAsync(HttpClient http, string uuid)
    {
        uuid = uuid.Replace("-", "").ToLowerInvariant();
        if (Cache.TryGetValue(uuid, out var cached))
            return cached;
        try
        {
            using var json = JsonDocument.Parse(await http.GetStringAsync(SessionApi + uuid));
            var texturesProperty = json.RootElement.GetProperty("properties").EnumerateArray()
                .First(p => p.GetProperty("name").GetString() == "textures");
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(texturesProperty.GetProperty("value").GetString()!));
            using var textures = JsonDocument.Parse(decoded);
            var all = textures.RootElement.GetProperty("textures");
            if (!all.TryGetProperty("SKIN", out var skin))
                return Cache[uuid] = null;
            var slim = skin.TryGetProperty("metadata", out var meta) && JsonFiles.String(meta, "model") == "slim";
            var png = await http.GetByteArrayAsync(skin.GetProperty("url").GetString());
            byte[]? cape = null;
            if (all.TryGetProperty("CAPE", out var capeElement))
            {
                try
                {
                    cape = await http.GetByteArrayAsync(capeElement.GetProperty("url").GetString());
                }
                catch (Exception ex)
                {
                    ErrorReport.Log("Umhang eines Spielers laden", ex);
                }
            }
            return Cache[uuid] = new PlayerSkin(png, slim, cape);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Skin eines Spielers laden", ex);
            return null;
        }
    }

    public static async Task<(string Uuid, string Name)?> LookUpAsync(HttpClient http, string name)
    {
        using var response = await http.GetAsync(LookupApi + Uri.EscapeDataString(name.Trim()));
        if (!response.IsSuccessStatusCode)
            return null;
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("id").GetString()!, json.RootElement.GetProperty("name").GetString()!);
    }
}
