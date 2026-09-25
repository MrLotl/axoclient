using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;

namespace AxoClient.Accounts;

public record CapeInfo(string Id, string Alias, bool Active, string? Url, byte[]? Png = null);

public class ProfileInfo
{
    public byte[]? SkinPng { get; init; }
    public bool SkinSlim { get; init; }
    public BitmapSource? Head { get; init; }
    public List<CapeInfo> Capes { get; init; } = [];

    public byte[]? ActiveCapePng => Capes.FirstOrDefault(c => c.Active)?.Png;
    public string? ActiveCapeName => Capes.FirstOrDefault(c => c.Active)?.Alias;
}

public record AccountTrouble(string What, Exception Error);

public sealed class AccountService(HttpClient http)
{
    private const string ProfileApi = "https://api.minecraftservices.com/minecraft/profile";

    private readonly JELoginHandler _login = JELoginHandlerBuilder.BuildDefault();

    public MSession? Session { get; private set; }
    public ProfileInfo? Profile { get; private set; }
    public AccountTrouble? Problem { get; private set; }

    public event Action? Changed;
    public event Action? SignedIn;
    public event Action? SignedOut;

    public string? CompactUuid => Session?.UUID?.Replace("-", "").ToLowerInvariant();

    public void NotifyChanged() => Changed?.Invoke();

    public void ReportProblem(string what, Exception? ex)
    {
        if (ex == null)
        {
            Problem = null;
            return;
        }
        ErrorReport.Log(what, ex);
        Problem = new AccountTrouble(what, ex);
    }

    public async Task TryRestoreAsync()
    {
        try
        {
            Session = await _login.AuthenticateSilently();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Gespeicherte Anmeldung wiederherstellen", ex);
            Session = null;
            Changed?.Invoke();
            return;
        }
        await RefreshProfileAsync();
        SignedIn?.Invoke();
    }

    public async Task LoginAsync()
    {
        Session = await _login.AuthenticateInteractively();
        await RefreshProfileAsync();
        SignedIn?.Invoke();
    }

    public async Task LogoutAsync()
    {
        await _login.Signout();
        Session = null;
        Profile = null;
        SignedOut?.Invoke();
        Changed?.Invoke();
    }

    public async Task<MSession> GetFreshSessionAsync()
    {
        Session = await _login.AuthenticateSilently();
        return Session;
    }

    public async Task RefreshProfileAsync()
    {
        try
        {
            using var json = await SendProfileRequestAsync(HttpMethod.Get, "");
            Profile = await LoadProfileAsync(json.RootElement);
            ReportProblem("", null);
        }
        catch (Exception ex)
        {
            ReportProblem("Dein Minecraft-Profil konnte nicht geladen werden", ex);
            Profile = null;
        }
        Changed?.Invoke();
    }

    public async Task UploadSkinAsync(byte[] png, bool slim)
    {
        await GetFreshSessionAsync();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(slim ? "slim" : "classic"), "variant");
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "skin.png");
        using var _ = await SendProfileRequestAsync(HttpMethod.Post, "/skins", form);
        await RefreshProfileAsync();
    }

    public async Task SetCapeAsync(string? capeId)
    {
        await GetFreshSessionAsync();
        HttpContent? body = capeId == null
            ? null
            : new StringContent(JsonSerializer.Serialize(new { capeId }), Encoding.UTF8, "application/json");
        using var _ = await SendProfileRequestAsync(capeId == null ? HttpMethod.Delete : HttpMethod.Put, "/capes/active", body);
        await RefreshProfileAsync();
    }

    private async Task<JsonDocument> SendProfileRequestAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, ProfileApi + path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session!.AccessToken);
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Minecraft-Dienste antworten mit {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private async Task<ProfileInfo> LoadProfileAsync(JsonElement root)
    {
        var activeSkin = root.TryGetProperty("skins", out var skins)
            ? skins.EnumerateArray().FirstOrDefault(s => s.GetProperty("state").GetString() == "ACTIVE")
            : default;
        var skinPng = activeSkin.ValueKind == JsonValueKind.Object
            ? await http.GetByteArrayAsync(activeSkin.GetProperty("url").GetString())
            : null;
        var slim = activeSkin.ValueKind == JsonValueKind.Object
                   && activeSkin.TryGetProperty("variant", out var variant) && variant.GetString() == "SLIM";

        var capes = root.TryGetProperty("capes", out var capeArray)
            ? capeArray.EnumerateArray().Select(cape => new CapeInfo(
                cape.GetProperty("id").GetString()!,
                cape.TryGetProperty("alias", out var alias) ? alias.GetString() ?? "Umhang" : "Umhang",
                cape.GetProperty("state").GetString() == "ACTIVE",
                cape.TryGetProperty("url", out var url) ? url.GetString() : null)).ToList()
            : [];

        return new ProfileInfo
        {
            SkinPng = skinPng,
            SkinSlim = slim,
            Head = skinPng != null ? SkinRenderer.RenderHead(skinPng) : null,
            Capes = (await Task.WhenAll(capes.Select(DownloadCapeAsync))).ToList()
        };
    }

    private async Task<CapeInfo> DownloadCapeAsync(CapeInfo cape)
    {
        if (cape.Url == null)
            return cape;
        try
        {
            return cape with { Png = await http.GetByteArrayAsync(cape.Url) };
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"Vorschau für Umhang \"{cape.Alias}\" laden", ex);
            return cape;
        }
    }
}
