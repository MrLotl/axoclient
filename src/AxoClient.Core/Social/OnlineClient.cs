using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AxoClient.Core.Accounts;
using AxoClient.Core.Common;

namespace AxoClient.Core.Social;

/// <summary>
/// Die einzige Stelle, die mit dem Online-Service spricht: Anmeldung per Mojang-Spielerzertifikat, eigenes
/// Geräte-Token (DPAPI), <c>Authorization</c>-Header, Fehler-Codes, stilles Neuanmelden bei 401.
/// Der Service bekommt nie ein Microsoft- oder Minecraft-Token.
/// </summary>
public sealed class OnlineClient(ApiClient api, AccountManager accounts, TokenStore tokens, EventHub events)
{
    private const string CertificatesUrl = "https://api.minecraftservices.com/player/certificates";
    private readonly SemaphoreSlim _registerLock = new(1, 1);
    private bool _available = true;

    public string BaseUrl { get; set; } = AppInfo.ServiceUrl + "/v2";

    /// <summary>War der Service bei der letzten Anfrage erreichbar? Sonst "nicht verfügbar", kein Dialog.</summary>
    public bool Available
    {
        get => _available;
        private set
        {
            if (_available == value)
                return;
            _available = value;
            events.Publish("service.availability", new { available = value });
        }
    }

    /// <summary>Hat der Server ein Launcher-Update erzwungen (<c>update_required</c>)?</summary>
    public bool UpdateRequired { get; private set; }

    private static string TokenKey(string uuid) => "service:" + uuid.Replace("-", "");

    public bool CanUse => accounts.Active is { Type: AccountType.Microsoft };

    public async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await SendRawAsync(method, path, body == null ? null : JsonContent(body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, Json.Options);
    }

    public async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var _ = await SendRawAsync(method, path, body == null ? null : JsonContent(body), ct);
    }

    public async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct,
        bool allowAnonymous = false)
    {
        if (!allowAnonymous && !CanUse)
            throw new AxoException("service.notSignedIn");
        for (var attempt = 0;; attempt++)
        {
            string? token = null;
            if (!allowAnonymous)
                token = tokens.GetString(TokenKey(accounts.Active!.Uuid)) ?? await RegisterAsync(ct);

            HttpResponseMessage response;
            try
            {
                var bytes = content == null ? null : await content.ReadAsByteArrayAsync(ct);
                var contentType = content?.Headers.ContentType;
                response = await api.SendAsync(() =>
                {
                    var request = new HttpRequestMessage(method, BaseUrl + path);
                    if (bytes != null)
                    {
                        request.Content = new ByteArrayContent(bytes);
                        request.Content.Headers.ContentType = contentType;
                    }
                    request.Headers.Add("X-AxoClient-Version", AppInfo.Version);
                    return request;
                }, ct, r =>
                {
                    if (token != null)
                        r.Headers.Authorization = ApiClient.Bearer(token);
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Available = false;
                throw new AxoException("service.unavailable", inner: ex);
            }
            Available = true;

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && !allowAnonymous)
            {
                // Token abgelaufen oder widerrufen: still neu anmelden
                response.Dispose();
                tokens.Remove(TokenKey(accounts.Active!.Uuid));
                continue;
            }
            if (response.IsSuccessStatusCode)
                return response;
            throw await ToErrorAsync(response, ct);
        }
    }

    private async Task<AxoException> ToErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string code = "service.error";
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var c) && c.GetString() is { } value
                && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
                code = "service." + value;
        }
        catch (JsonException)
        {
        }
        if (code == "service.update_required")
        {
            UpdateRequired = true;
            events.Publish("service.updateRequired");
        }
        response.Dispose();
        return new AxoException(code, new() { ["status"] = (int)response.StatusCode });
    }

    /// <summary>Anmeldung beim Service mit einem Mojang-Spielerzertifikat. Gibt das neue Geräte-Token zurück.</summary>
    public async Task<string> RegisterAsync(CancellationToken ct)
    {
        await _registerLock.WaitAsync(ct);
        try
        {
            var account = accounts.Active ?? throw new AxoException("service.notSignedIn");
            if (tokens.GetString(TokenKey(account.Uuid)) is { } existing)
                return existing;

            var session = await accounts.GetSessionAsync(ct);
            using var certResponse = await api.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, CertificatesUrl)
            {
                Content = new ByteArrayContent([]) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }
            }, ct, r => r.Headers.Authorization = ApiClient.Bearer(session.AccessToken));
            await ApiClient.EnsureSuccessAsync(certResponse, ct);
            using var cert = JsonDocument.Parse(await certResponse.Content.ReadAsStringAsync(ct));
            var root = cert.RootElement;
            var keyPair = root.GetProperty("keyPair");

            using var rsa = RSA.Create();
            rsa.ImportFromPem(keyPair.GetProperty("privateKey").GetString());
            var uuid = session.CompactUuid;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var signature = rsa.SignData(Encoding.UTF8.GetBytes($"axoclient:register:{uuid}:{timestamp}"),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            using var response = await api.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/auth/register")
            {
                Content = JsonContent(new
                {
                    uuid,
                    name = session.Name,
                    publicKey = PemBody(keyPair.GetProperty("publicKey").GetString()!),
                    publicKeySignature = root.GetProperty("publicKeySignatureV2").GetString(),
                    expiresAt = DateTimeOffset.Parse(root.GetProperty("expiresAt").GetString()!, CultureInfo.InvariantCulture)
                        .ToUnixTimeMilliseconds(),
                    timestamp,
                    signature = Convert.ToBase64String(signature),
                    device = Text.Clean(Environment.MachineName, 40)
                })
            }, ct);
            if (!response.IsSuccessStatusCode)
                throw await ToErrorAsync(response, ct);
            using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var token = answer.RootElement.GetProperty("token").GetString()!;
            tokens.Set(TokenKey(account.Uuid), token);
            return token;
        }
        catch (HttpRequestException ex)
        {
            Available = false;
            throw new AxoException("service.unavailable", inner: ex);
        }
        finally
        {
            _registerLock.Release();
        }
    }

    /// <summary>Geräte-Token des aktiven Kontos (meldet bei Bedarf neu an), z. B. für den Echtzeit-Kanal.</summary>
    public async Task<string> TokenAsync(CancellationToken ct)
    {
        if (!CanUse)
            throw new AxoException("service.notSignedIn");
        return tokens.GetString(TokenKey(accounts.Active!.Uuid)) ?? await RegisterAsync(ct);
    }

    /// <summary>Token verwerfen (vom Service abgelehnt); die nächste Anfrage meldet still neu an.</summary>
    public void ForgetToken()
    {
        if (accounts.Active is { } account)
            tokens.Remove(TokenKey(account.Uuid));
    }

    /// <summary>Dieses Gerät abmelden (Token beim Service widerrufen und lokal löschen).</summary>
    public async Task SignOutDeviceAsync(string uuid, CancellationToken ct)
    {
        if (tokens.GetString(TokenKey(uuid)) == null)
            return;
        try
        {
            if (accounts.Active?.Uuid == uuid)
                await SendAsync(HttpMethod.Delete, "/auth/token", ct: ct);
        }
        catch (AxoException)
        {
        }
        tokens.Remove(TokenKey(uuid));
    }

    private static string PemBody(string pem) =>
        string.Concat(pem.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("-----")));

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body, Json.Compact), Encoding.UTF8, "application/json");
}
