using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;
using AxoClient.DataModel.Skins;
using AxoClient.Skins.Contract;

namespace AxoClient.Skins
{
    /// <summary>Mojang-Dienste für Skins und Capes: eigenes Profil ändern, fremde Spieler nachschlagen.</summary>
    public sealed class MojangApi : IMojangApi
    {
        private readonly IApiClient _api;
        private readonly IAccountManager _accounts;
        private readonly ISkinFiles _skinFiles;
        private readonly ITextHelper _textHelper;
        private readonly IUuidHelper _uuidHelper;
        private const string ProfileApi = "https://api.minecraftservices.com/minecraft/profile";
        private const string SessionApi = "https://sessionserver.mojang.com/session/minecraft/profile/";
        private const string LookupApi = "https://api.mojang.com/users/profiles/minecraft/";

        public MojangApi(
            IApiClient api,
            IAccountManager accounts,
            ISkinFiles skinFiles,
            ITextHelper textHelper,
            IUuidHelper uuidHelper)
        {
            _api = api;
            _accounts = accounts;
            _skinFiles = skinFiles;
            _textHelper = textHelper;
            _uuidHelper = uuidHelper;
        }

        public async Task<MojangProfile> GetOwnProfile(CancellationToken ct)
        {
            using var doc = await sendProfile(HttpMethod.Get, "", null, ct);
            return parseProfile(doc.RootElement);
        }

        public async Task<MojangProfile> UploadSkin(byte[] png, SkinModel model, CancellationToken ct)
        {
            if (!_skinFiles.IsValidSkin(png))
            {
                throw new AxoException("skins.invalidImage");
            }

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(model == SkinModel.Slim ? "slim" : "classic"), "variant");
            var file = new ByteArrayContent(png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "skin.png");
            using var doc = await sendProfile(HttpMethod.Post, "/skins", form, ct);
            return await GetOwnProfile(ct);
        }

        /// <summary>Mojang-Cape setzen oder (mit <c>null</c>) ablegen.</summary>
        public async Task<MojangProfile> SetCape(string? capeId, CancellationToken ct)
        {
            HttpContent? body = capeId == null
                ? null
                : new StringContent(JsonSerializer.Serialize(new { capeId }), Encoding.UTF8, "application/json");
            using var doc = await sendProfile(capeId == null ? HttpMethod.Delete : HttpMethod.Put, "/capes/active", body, ct);
            return await GetOwnProfile(ct);
        }

        /// <summary>Skin und Cape eines beliebigen Spielers (für "Von Spieler übernehmen" und Köpfe).</summary>
        public async Task<PlayerTextures?> GetTextures(string uuid, CancellationToken ct)
        {
            var compact = uuid.Replace("-", "").ToLowerInvariant();
            if (compact.Length != 32 || !compact.All(Uri.IsHexDigit))
            {
                return null;
            }

            try
            {
                using var json = JsonDocument.Parse(await _api.GetString(SessionApi + compact, TimeSpan.FromMinutes(30), ct));
                var name = json.RootElement.GetProperty("name").GetString() ?? "";
                var textures = json.RootElement.GetProperty("properties").EnumerateArray()
                    .First(p => p.GetProperty("name").GetString() == "textures");
                using var decoded = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(textures.GetProperty("value").GetString()!)));
                var all = decoded.RootElement.GetProperty("textures");
                string? skinUrl = null, capeUrl = null;
                var model = SkinModel.Classic;
                if (all.TryGetProperty("SKIN", out var skin))
                {
                    skinUrl = httpsTexture(skin.GetProperty("url").GetString());
                    model = skin.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("model", out var m) && m.GetString() == "slim"
                        ? SkinModel.Slim
                        : SkinModel.Classic;
                }
                if (all.TryGetProperty("CAPE", out var cape))
                {
                    capeUrl = httpsTexture(cape.GetProperty("url").GetString());
                }

                return new PlayerTextures(_uuidHelper.FormatUuid(compact), name, skinUrl, model, capeUrl);
            }
            catch (Exception ex) when (ex is JsonException or AxoException or HttpRequestException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }

        public async Task<(string Uuid, string Name)?> LookUp(string name, CancellationToken ct)
        {
            var clean = name.Trim();
            if (clean.Length is < 3 or > 16 || !clean.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                return null;
            }

            try
            {
                using var json = JsonDocument.Parse(await _api.GetString(LookupApi + Uri.EscapeDataString(clean), TimeSpan.FromHours(1), ct));
                return (_uuidHelper.FormatUuid(json.RootElement.GetProperty("id").GetString()!),
                    json.RootElement.GetProperty("name").GetString()!);
            }
            catch (AxoException ex) when (ex.Code == "errors.notFound")
            {
                return null;
            }
        }

        /// <summary>Lädt eine Textur von Mojang (nur textures.minecraft.net).</summary>
        public async Task<byte[]> DownloadTexture(string url, CancellationToken ct)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "textures.minecraft.net")
            {
                throw new AxoException("errors.insecureUrl", new() { ["host"] = uri?.Host ?? "" });
            }

            using var response = await _api.Send(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            await _api.EnsureSuccess(response, ct);
            return await response.Content.ReadAsByteArrayAsync(ct);
        }

        private string? httpsTexture(string? url) =>
            url == null ? null : url.StartsWith("http://textures.minecraft.net/") ? "https://" + url["http://".Length..] : url;

        private MojangProfile parseProfile(JsonElement root)
        {
            var skins = root.TryGetProperty("skins", out var s) ? s.EnumerateArray().ToList() : [];
            var active = skins.FirstOrDefault(x => x.GetProperty("state").GetString() == "ACTIVE");
            var capes = root.TryGetProperty("capes", out var c)
                ? c.EnumerateArray().Select(x => new MojangCape(x.GetProperty("id").GetString()!,
                    _textHelper.Clean(x.TryGetProperty("alias", out var a) ? a.GetString() : "Cape", 60),
                    x.GetProperty("state").GetString() == "ACTIVE",
                    httpsTexture(x.TryGetProperty("url", out var u) ? u.GetString() : null))).ToList()
                : [];
            return new MojangProfile(_uuidHelper.FormatUuid(root.GetProperty("id").GetString()!), root.GetProperty("name").GetString()!,
                active.ValueKind == JsonValueKind.Object ? httpsTexture(active.GetProperty("url").GetString()) : null,
                active.ValueKind == JsonValueKind.Object && active.TryGetProperty("variant", out var v) && v.GetString() == "SLIM"
                    ? SkinModel.Slim
                    : SkinModel.Classic,
                capes);
        }

        private async Task<JsonDocument> sendProfile(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
        {
            var session = await _accounts.GetSession(ct);
            if (session.Type == AccountType.Dev)
            {
                throw new AxoException("skins.devAccount");
            }

            using var response = await _api.Send(() => new HttpRequestMessage(method, ProfileApi + path) { Content = content }, ct,
                r => r.Headers.Authorization = _api.Bearer(session.AccessToken));
            await _api.EnsureSuccess(response, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
    }
}
