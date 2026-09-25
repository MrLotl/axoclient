using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AxoClient.Online;

public class AxoService(AppServices app)
{
    private const string CertificatesUrl = "https://api.minecraftservices.com/player/certificates";
    private const string ServiceOutdated =
        "Der AxoClient-Dienst ist veraltet: bitte den aktuellen Code aus service/worker.js deployen.";

    private static string Api => AppInfo.AxoServiceUrl.TrimEnd('/');

    public bool Available => app.Settings.BadgeEnabled && app.Accounts.Session != null;

    public async Task RegisterAsync()
    {
        var session = await app.Accounts.GetFreshSessionAsync();
        app.Settings.BadgeToken = await RegisterSessionAsync(session.AccessToken!, session.UUID!, session.Username);
        app.Settings.BadgeTokenUuid = session.UUID;
        app.SaveSettings();
    }

    public async Task RegisterQuietlyAsync()
    {
        if (!Available)
            return;
        try
        {
            await RegisterAsync();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Beim AxoClient-Dienst anmelden", ex);
        }
    }

    private async Task<string?> RegisterSessionAsync(string accessToken, string sessionUuid, string? username)
    {
        var emptyJson = new ByteArrayContent([]);
        emptyJson.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var certRequest = new HttpRequestMessage(HttpMethod.Post, CertificatesUrl) { Content = emptyJson };
        certRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var certResponse = await app.Http.SendAsync(certRequest);
        if (!certResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Mojang hat kein Spieler-Zertifikat ausgestellt (Status {(int)certResponse.StatusCode}).");
        using var cert = JsonDocument.Parse(await certResponse.Content.ReadAsStringAsync());
        var root = cert.RootElement;
        var keyPair = root.GetProperty("keyPair");

        var publicKey = PemBody(keyPair.GetProperty("publicKey").GetString()!);
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(PemBody(keyPair.GetProperty("privateKey").GetString()!), out _);
        var expiresAt = DateTimeOffset.Parse(root.GetProperty("expiresAt").GetString()!, CultureInfo.InvariantCulture)
            .ToUnixTimeMilliseconds();

        var uuid = sessionUuid.Replace("-", "").ToLowerInvariant();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var signature = rsa.SignData(Encoding.UTF8.GetBytes($"mclauncher-badge:register:{uuid}:{timestamp}"),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        using var register = await app.Http.PostAsJsonAsync(Api + "/register", new
        {
            uuid,
            name = username,
            publicKey = Convert.ToBase64String(publicKey),
            expiresAt,
            keySignature = root.GetProperty("publicKeySignatureV2").GetString(),
            timestamp,
            signature = Convert.ToBase64String(signature)
        });
        var answer = await register.Content.ReadAsStringAsync();
        if (!register.IsSuccessStatusCode)
            throw new InvalidOperationException($"Der Dienst hat die Anmeldung abgelehnt: {answer}");

        using var json = JsonDocument.Parse(answer);
        return json.RootElement.TryGetProperty("token", out var token) ? token.GetString() : null;
    }

    private static byte[] PemBody(string pem) =>
        Convert.FromBase64String(string.Concat(pem.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("-----"))));

    public async Task<List<FriendInfo>> GetFriendsAsync()
    {
        using var json = await PostAsync("/friends", new());
        return json.RootElement.GetProperty("friends").EnumerateArray().Select(f => new FriendInfo
            {
                Uuid = f.GetProperty("uuid").GetString()!,
                Name = f.GetProperty("name").GetString()!,
                State = f.GetProperty("state").GetString()!,
                Playing = f.GetProperty("playing").GetBoolean(),
                Server = f.GetProperty("server").GetString(),
                Version = f.GetProperty("version").GetString()
            })
            .OrderBy(f => f.SortKey).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<FriendInfo>> GetMutualFriendsAsync() =>
        (await GetFriendsAsync()).Where(f => f.IsFriend).ToList();

    public async Task<string> AddFriendAsync(string name)
    {
        using var json = await PostAsync("/friends/add", new() { ["name"] = name });
        return json.RootElement.GetProperty("name").GetString()!;
    }

    public async Task RemoveFriendAsync(string uuid)
    {
        using var _ = await PostAsync("/friends/remove", new() { ["uuid"] = uuid });
    }

    public async Task SetStatusAsync(bool playing, string? server, string? version)
    {
        using var _ = await PostAsync("/status", new()
        {
            ["playing"] = playing,
            ["server"] = server,
            ["version"] = version
        });
    }

    public async Task<string?> GetCapeAsync()
    {
        using var json = await PostAsync("/cape", new());
        return json.RootElement.GetProperty("cape").GetString();
    }

    public async Task SetCapeAsync(string? capeId)
    {
        using var _ = await PostAsync("/cape", new() { ["cape"] = capeId });
    }

    public async Task SendShareAsync(string toUuid, string kind, string title, string payload)
    {
        using var _ = await ShareCallAsync("/share/send", new()
        {
            ["to"] = toUuid,
            ["kind"] = kind,
            ["title"] = title,
            ["payload"] = payload
        });
    }

    public async Task<List<ShareInfo>> GetInboxAsync()
    {
        using var json = await ShareCallAsync("/share/inbox", new());
        return json.RootElement.GetProperty("shares").EnumerateArray().Select(s => new ShareInfo
        {
            Id = s.GetProperty("id").GetInt64(),
            FromUuid = s.GetProperty("fromUuid").GetString()!,
            FromName = s.GetProperty("fromName").GetString()!,
            Kind = s.GetProperty("kind").GetString()!,
            Title = Sanitize.Text(s.GetProperty("title").GetString(), 100),
            Size = s.GetProperty("size").GetInt32(),
            CreatedUtc = DateTimeOffset.FromUnixTimeMilliseconds(s.GetProperty("created").GetInt64()).UtcDateTime
        }).ToList();
    }

    public async Task<string> GetSharePayloadAsync(long id)
    {
        using var json = await ShareCallAsync("/share/get", new() { ["id"] = id });
        return json.RootElement.GetProperty("payload").GetString()!;
    }

    public async Task DeleteShareAsync(long id)
    {
        using var _ = await ShareCallAsync("/share/delete", new() { ["id"] = id });
    }

    private async Task<JsonDocument> ShareCallAsync(string path, Dictionary<string, object?> body)
    {
        try
        {
            return await PostAsync(path, body);
        }
        catch (InvalidOperationException ex) when (ex.Message == "Nicht gefunden")
        {
            throw new InvalidOperationException(
                "Der AxoClient-Dienst kennt das Teilen noch nicht. Bitte den aktuellen Code aus service/worker.js deployen " +
                "(siehe service/ANLEITUNG.md).");
        }
    }

    private async Task<JsonDocument> PostAsync(string path, Dictionary<string, object?> body, bool retried = false)
    {
        if (!Available)
            throw new InvalidOperationException("Nicht angemeldet oder AxoClient-Dienst nicht eingerichtet.");
        var settings = app.Settings;
        if (settings.BadgeToken == null || settings.BadgeTokenUuid != app.Accounts.Session!.UUID)
            await RegisterAsync();
        if (settings.BadgeToken == null)
            throw new InvalidOperationException(ServiceOutdated);

        body["token"] = settings.BadgeToken;
        using var response = await app.Http.PostAsJsonAsync(Api + path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.Unauthorized && !retried)
        {
            settings.BadgeToken = null;
            return await PostAsync(path, body, retried: true);
        }
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ErrorMessageOf(text) ?? $"Der Dienst antwortet mit Status {(int)response.StatusCode}.");
        return JsonDocument.Parse(text);
    }

    private static string? ErrorMessageOf(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.GetProperty("error").GetString();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Fehlermeldung des Dienstes lesen", ex);
            return null;
        }
    }
}
