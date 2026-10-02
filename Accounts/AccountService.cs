using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using XboxAuthNet.Game.Accounts;

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

public record SavedAccount(string Id, string Name, string? Uuid, BitmapSource? Head, bool Active, DateTime LastUsed = default);

public sealed class AccountService(HttpClient http)
{
    private const string ProfileApi = "https://api.minecraftservices.com/minecraft/profile";

    private readonly JELoginHandler _login = JELoginHandlerBuilder.BuildDefault();
    private IXboxGameAccount? _current;

    public MSession? Session { get; private set; }
    public ProfileInfo? Profile { get; private set; }
    public AccountTrouble? Problem { get; private set; }
    public IReadOnlyList<SavedAccount> Saved { get; private set; } = [];

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

    public bool HasSavedAccounts => _login.AccountManager.GetAccounts().Any(a => a is JEGameAccount { Profile: not null });

    public async Task TryRestoreAsync()
    {
        try
        {
            _current = _login.AccountManager.GetDefaultAccount();
            Session = await _login.AuthenticateSilently(_current);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Gespeicherte Anmeldung wiederherstellen", ex);
            Session = null;
            LoadSaved();
            Changed?.Invoke();
            return;
        }
        LoadSaved();
        await RefreshProfileAsync();
        SignedIn?.Invoke();
    }

    public async Task LoginAsync()
    {
        var account = _login.AccountManager.NewAccount();
        var session = await _login.AuthenticateInteractively(account);
        if (Session != null)
            SignedOut?.Invoke();
        _current = account;
        Session = session;
        LoadSaved();
        await RefreshProfileAsync();
        SignedIn?.Invoke();
    }

    public async Task SwitchAsync(string id)
    {
        var account = _login.AccountManager.GetAccounts().FirstOrDefault(a => a.Identifier == id);
        if (account == null || account == _current && Session != null)
            return;
        MSession session;
        try
        {
            session = await _login.AuthenticateSilently(account);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Konto wechseln", ex);
            session = await _login.AuthenticateInteractively(account);
        }
        if (Session != null)
            SignedOut?.Invoke();
        _current = account;
        Session = session;
        LoadSaved();
        await RefreshProfileAsync();
        SignedIn?.Invoke();
    }

    public async Task LogoutAsync()
    {
        if (_current != null)
            await _login.Signout(_current);
        else
            await _login.Signout();
        Session = null;
        Profile = null;
        _current = null;
        SignedOut?.Invoke();

        var next = _login.AccountManager.GetAccounts().OfType<JEGameAccount>()
            .Where(a => a.Profile != null)
            .OrderByDescending(a => a.LastAccess)
            .FirstOrDefault();
        if (next != null)
        {
            try
            {
                _current = next;
                Session = await _login.AuthenticateSilently(next);
                LoadSaved();
                await RefreshProfileAsync();
                SignedIn?.Invoke();
                return;
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Nächstes Konto anmelden", ex);
                _current = null;
                Session = null;
            }
        }
        LoadSaved();
        Changed?.Invoke();
    }

    public async Task<MSession> GetFreshSessionAsync()
    {
        Session = _current != null ? await _login.AuthenticateSilently(_current) : await _login.AuthenticateSilently();
        return Session;
    }

    private void LoadSaved()
    {
        Saved = _login.AccountManager.GetAccounts().OfType<JEGameAccount>()
            .Where(a => a.Profile?.Username != null)
            .OrderByDescending(a => a.LastAccess)
            .Select(a => new SavedAccount(a.Identifier ?? a.Profile!.Username!, a.Profile!.Username!, a.Profile.UUID,
                LoadCachedHead(a.Profile.UUID), Session != null && a.Profile.UUID == Session.UUID, a.LastAccess))
            .ToList();
    }

    private static string? HeadCachePath(string? uuid) =>
        string.IsNullOrEmpty(uuid) ? null : Path.Combine(AppPaths.LauncherDir, "accounts", uuid.Replace("-", "") + ".png");

    private static BitmapSource? LoadCachedHead(string? uuid)
    {
        try
        {
            return HeadCachePath(uuid) is { } path && File.Exists(path) ? SkinRenderer.RenderHead(File.ReadAllBytes(path)) : null;
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Gespeicherten Kopf laden", ex);
            return null;
        }
    }

    private void CacheSkin(byte[] png)
    {
        try
        {
            if (HeadCachePath(Session?.UUID) is not { } path)
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, png);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Kopf zwischenspeichern", ex);
        }
    }

    public async Task RefreshProfileAsync()
    {
        try
        {
            using var json = await SendProfileRequestAsync(HttpMethod.Get, "");
            Profile = await LoadProfileAsync(json.RootElement);
            if (Profile.SkinPng != null)
            {
                CacheSkin(Profile.SkinPng);
                Saved = Saved.Select(a => a.Active ? a with { Head = Profile.Head } : a).ToList();
            }
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
