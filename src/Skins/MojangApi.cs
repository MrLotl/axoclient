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

        public async Task<MojangProfile> GetOwnProfile(CancellationToken cancellationToken)
        {
            using var document = await sendProfile(HttpMethod.Get, "", null, cancellationToken);
            return parseProfile(document.RootElement);
        }

        public async Task<MojangProfile> UploadSkin(byte[] png, SkinModel model, CancellationToken cancellationToken)
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
            using var document = await sendProfile(HttpMethod.Post, "/skins", form, cancellationToken);
            return await GetOwnProfile(cancellationToken);
        }

        /// <summary>Mojang-Cape setzen oder (mit <c>null</c>) ablegen.</summary>
        public async Task<MojangProfile> SetCape(string? capeId, CancellationToken cancellationToken)
        {
            HttpContent? body = capeId == null
                ? null
                : new StringContent(JsonSerializer.Serialize(new { capeId }), Encoding.UTF8, "application/json");
            using var document = await sendProfile(capeId == null ? HttpMethod.Delete : HttpMethod.Put, "/capes/active", body, cancellationToken);
            return await GetOwnProfile(cancellationToken);
        }

        /// <summary>Skin und Cape eines beliebigen Spielers (für "Von Spieler übernehmen" und Köpfe).</summary>
        public async Task<PlayerTextures?> GetTextures(string uuid, CancellationToken cancellationToken)
        {
            var compact = uuid.Replace("-", "").ToLowerInvariant();
            if (compact.Length != 32 || !compact.All(Uri.IsHexDigit))
            {
                return null;
            }

            try
            {
                using var json = JsonDocument.Parse(await _api.GetString(SessionApi + compact, TimeSpan.FromMinutes(30), cancellationToken));
                var name = json.RootElement.GetProperty("name").GetString() ?? "";
                var textures = json.RootElement.GetProperty("properties").EnumerateArray()
                    .First(property => property.GetProperty("name").GetString() == "textures");
                using var decoded = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(textures.GetProperty("value").GetString()!)));
                var all = decoded.RootElement.GetProperty("textures");
                string? skinUrl = null, capeUrl = null;
                var model = SkinModel.Classic;
                if (all.TryGetProperty("SKIN", out var skin))
                {
                    skinUrl = httpsTexture(skin.GetProperty("url").GetString());
                    model = skin.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("model", out var modelElement) && modelElement.GetString() == "slim"
                        ? SkinModel.Slim
                        : SkinModel.Classic;
                }
                if (all.TryGetProperty("CAPE", out var cape))
                {
                    capeUrl = httpsTexture(cape.GetProperty("url").GetString());
                }

                return new PlayerTextures(_uuidHelper.FormatUuid(compact), name, skinUrl, model, capeUrl);
            }
            catch (Exception exception) when (exception is JsonException or AxoException or HttpRequestException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }

        public async Task<(string Uuid, string Name)?> LookUp(string name, CancellationToken cancellationToken)
        {
            var clean = name.Trim();
            if (clean.Length is < 3 or > 16 || !clean.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
            {
                return null;
            }

            try
            {
                using var json = JsonDocument.Parse(await _api.GetString(LookupApi + Uri.EscapeDataString(clean), TimeSpan.FromHours(1), cancellationToken));
                return (_uuidHelper.FormatUuid(json.RootElement.GetProperty("id").GetString()!),
                    json.RootElement.GetProperty("name").GetString()!);
            }
            catch (AxoException exception) when (exception.Code == "errors.notFound")
            {
                return null;
            }
        }

        /// <summary>Lädt eine Textur von Mojang (nur textures.minecraft.net).</summary>
        public async Task<byte[]> DownloadTexture(string url, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "textures.minecraft.net")
            {
                throw new AxoException("errors.insecureUrl", new() { ["host"] = uri?.Host ?? "" });
            }

            using var response = await _api.Send(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
            await _api.EnsureSuccess(response, cancellationToken);
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        private string? httpsTexture(string? url) =>
            url == null ? null : url.StartsWith("http://textures.minecraft.net/") ? "https://" + url["http://".Length..] : url;

        private MojangProfile parseProfile(JsonElement root)
        {
            var skins = root.TryGetProperty("skins", out var skinsElement) ? skinsElement.EnumerateArray().ToList() : [];
            var active = skins.FirstOrDefault(skin => skin.GetProperty("state").GetString() == "ACTIVE");
            var capes = root.TryGetProperty("capes", out var capesElement)
                ? capesElement.EnumerateArray().Select(capeElement => new MojangCape(capeElement.GetProperty("id").GetString()!,
                    _textHelper.Clean(capeElement.TryGetProperty("alias", out var alias) ? alias.GetString() : "Cape", 60),
                    capeElement.GetProperty("state").GetString() == "ACTIVE",
                    httpsTexture(capeElement.TryGetProperty("url", out var url) ? url.GetString() : null))).ToList()
                : [];
            return new MojangProfile(_uuidHelper.FormatUuid(root.GetProperty("id").GetString()!), root.GetProperty("name").GetString()!,
                active.ValueKind == JsonValueKind.Object ? httpsTexture(active.GetProperty("url").GetString()) : null,
                active.ValueKind == JsonValueKind.Object && active.TryGetProperty("variant", out var variant) && variant.GetString() == "SLIM"
                    ? SkinModel.Slim
                    : SkinModel.Classic,
                capes);
        }

        private async Task<JsonDocument> sendProfile(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
        {
            var session = await _accounts.GetSession(cancellationToken);
            if (session.Type == AccountType.Dev)
            {
                throw new AxoException("skins.devAccount");
            }

            using var response = await _api.Send(() => new HttpRequestMessage(method, ProfileApi + path) { Content = content }, cancellationToken,
                request => request.Headers.Authorization = _api.Bearer(session.AccessToken));
            await _api.EnsureSuccess(response, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
    }
}
