using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AxoClient.Core.Accounts;
using AxoClient.Core.Common;

namespace AxoClient.Core.Skins;

public enum SkinModel
{
    Classic,
    Slim
}

public sealed record MojangCape(string Id, string Alias, bool Active, string? Url);

public sealed record MojangProfile(string Uuid, string Name, string? SkinUrl, SkinModel Model, IReadOnlyList<MojangCape> Capes);

public sealed record PlayerTextures(string Uuid, string Name, string? SkinUrl, SkinModel Model, string? CapeUrl);

/// <summary>Mojang-Dienste für Skins und Capes: eigenes Profil ändern, fremde Spieler nachschlagen.</summary>
public sealed class MojangApi(ApiClient api, AccountManager accounts)
{
    private const string ProfileApi = "https://api.minecraftservices.com/minecraft/profile";
    private const string SessionApi = "https://sessionserver.mojang.com/session/minecraft/profile/";
    private const string LookupApi = "https://api.mojang.com/users/profiles/minecraft/";

    public async Task<MojangProfile> GetOwnProfileAsync(CancellationToken ct)
    {
        using var doc = await SendProfileAsync(HttpMethod.Get, "", null, ct);
        return ParseProfile(doc.RootElement);
    }

    public async Task<MojangProfile> UploadSkinAsync(byte[] png, SkinModel model, CancellationToken ct)
    {
        if (!SkinFiles.IsValidSkin(png))
            throw new AxoException("skins.invalidImage");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(model == SkinModel.Slim ? "slim" : "classic"), "variant");
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "skin.png");
        using var doc = await SendProfileAsync(HttpMethod.Post, "/skins", form, ct);
        return await GetOwnProfileAsync(ct);
    }

    /// <summary>Mojang-Cape setzen oder (mit <c>null</c>) ablegen.</summary>
    public async Task<MojangProfile> SetCapeAsync(string? capeId, CancellationToken ct)
    {
        HttpContent? body = capeId == null
            ? null
            : new StringContent(JsonSerializer.Serialize(new { capeId }), Encoding.UTF8, "application/json");
        using var doc = await SendProfileAsync(capeId == null ? HttpMethod.Delete : HttpMethod.Put, "/capes/active", body, ct);
        return await GetOwnProfileAsync(ct);
    }

    /// <summary>Skin und Cape eines beliebigen Spielers (für "Von Spieler übernehmen" und Köpfe).</summary>
    public async Task<PlayerTextures?> GetTexturesAsync(string uuid, CancellationToken ct)
    {
        var compact = uuid.Replace("-", "").ToLowerInvariant();
        if (compact.Length != 32 || !compact.All(Uri.IsHexDigit))
            return null;
        try
        {
            using var json = JsonDocument.Parse(await api.GetStringAsync(SessionApi + compact, TimeSpan.FromMinutes(30), ct));
            var name = json.RootElement.GetProperty("name").GetString() ?? "";
            var textures = json.RootElement.GetProperty("properties").EnumerateArray()
                .First(p => p.GetProperty("name").GetString() == "textures");
            using var decoded = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(textures.GetProperty("value").GetString()!)));
            var all = decoded.RootElement.GetProperty("textures");
            string? skinUrl = null, capeUrl = null;
            var model = SkinModel.Classic;
            if (all.TryGetProperty("SKIN", out var skin))
            {
                skinUrl = HttpsTexture(skin.GetProperty("url").GetString());
                model = skin.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("model", out var m) && m.GetString() == "slim"
                    ? SkinModel.Slim
                    : SkinModel.Classic;
            }
            if (all.TryGetProperty("CAPE", out var cape))
                capeUrl = HttpsTexture(cape.GetProperty("url").GetString());
            return new PlayerTextures(MicrosoftAuthProvider.FormatUuid(compact), name, skinUrl, model, capeUrl);
        }
        catch (Exception ex) when (ex is JsonException or AxoException or HttpRequestException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public async Task<(string Uuid, string Name)?> LookUpAsync(string name, CancellationToken ct)
    {
        var clean = name.Trim();
        if (clean.Length is < 3 or > 16 || !clean.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return null;
        try
        {
            using var json = JsonDocument.Parse(await api.GetStringAsync(LookupApi + Uri.EscapeDataString(clean), TimeSpan.FromHours(1), ct));
            return (MicrosoftAuthProvider.FormatUuid(json.RootElement.GetProperty("id").GetString()!),
                json.RootElement.GetProperty("name").GetString()!);
        }
        catch (AxoException ex) when (ex.Code == "errors.notFound")
        {
            return null;
        }
    }

    /// <summary>Lädt eine Textur von Mojang (nur textures.minecraft.net).</summary>
    public async Task<byte[]> DownloadTextureAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "textures.minecraft.net")
            throw new AxoException("errors.insecureUrl", new() { ["host"] = uri?.Host ?? "" });
        using var response = await api.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        await ApiClient.EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static string? HttpsTexture(string? url) =>
        url == null ? null : url.StartsWith("http://textures.minecraft.net/") ? "https://" + url["http://".Length..] : url;

    private static MojangProfile ParseProfile(JsonElement root)
    {
        var skins = root.TryGetProperty("skins", out var s) ? s.EnumerateArray().ToList() : [];
        var active = skins.FirstOrDefault(x => x.GetProperty("state").GetString() == "ACTIVE");
        var capes = root.TryGetProperty("capes", out var c)
            ? c.EnumerateArray().Select(x => new MojangCape(x.GetProperty("id").GetString()!,
                Text.Clean(x.TryGetProperty("alias", out var a) ? a.GetString() : "Cape", 60),
                x.GetProperty("state").GetString() == "ACTIVE",
                HttpsTexture(x.TryGetProperty("url", out var u) ? u.GetString() : null))).ToList()
            : [];
        return new MojangProfile(MicrosoftAuthProvider.FormatUuid(root.GetProperty("id").GetString()!), root.GetProperty("name").GetString()!,
            active.ValueKind == JsonValueKind.Object ? HttpsTexture(active.GetProperty("url").GetString()) : null,
            active.ValueKind == JsonValueKind.Object && active.TryGetProperty("variant", out var v) && v.GetString() == "SLIM"
                ? SkinModel.Slim
                : SkinModel.Classic,
            capes);
    }

    private async Task<JsonDocument> SendProfileAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        var session = await accounts.GetSessionAsync(ct);
        if (session.Type == AccountType.Dev)
            throw new AxoException("skins.devAccount");
        using var response = await api.SendAsync(() => new HttpRequestMessage(method, ProfileApi + path) { Content = content }, ct,
            r => r.Headers.Authorization = ApiClient.Bearer(session.AccessToken));
        await ApiClient.EnsureSuccessAsync(response, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }
}

public static class SkinFiles
{
    public const int MaxBytes = 256 * 1024;

    /// <summary>Gültiger Skin: PNG, 64×64 oder 64×32.</summary>
    public static bool IsValidSkin(byte[] png) =>
        png.Length <= MaxBytes && Instances.ImageFormats.PngSize(png) is (64, 64) or (64, 32);

    /// <summary>Gültiges Cape: PNG 64×32 oder ein Vielfaches bis 2048×1024, höchstens 1,5 MB.</summary>
    public static bool IsValidCape(byte[] png)
    {
        if (png.Length > 1_572_864 || Instances.ImageFormats.PngSize(png) is not var (w, h))
            return false;
        return w * 32 == h * 64 && w >= 64 && w <= 2048 && (w & (w - 1)) == 0;
    }

    public static string IdOf(byte[] png) => Text.Sha256Hex(png)[..12];
}
