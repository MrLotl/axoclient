using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;
using AxoClient.Social.Contract;

namespace AxoClient.Social
{
    /// <summary>
    /// Die einzige Stelle, die mit dem Online-Service spricht: Anmeldung per Mojang-Spielerzertifikat, eigenes
    /// Geräte-Token (DPAPI), <c>Authorization</c>-Header, Fehler-Codes, stilles Neuanmelden bei 401.
    /// Der Service bekommt nie ein Microsoft- oder Minecraft-Token.
    /// </summary>
    public sealed class OnlineClient : IOnlineClient
    {
        private readonly IAppInfo _appInfo;
        private readonly IJsonHelper _jsonHelper;
        private readonly ITextHelper _textHelper;
        private readonly IApiClient _api;
        private readonly IAccountManager _accounts;
        private readonly ITokenStore _tokens;
        private readonly IEventHub _events;
        private const string CertificatesUrl = "https://api.minecraftservices.com/player/certificates";
        private readonly SemaphoreSlim _registerLock = new(1, 1);
        private bool _available = true;

        public OnlineClient(
            IApiClient api,
            IAccountManager accounts,
            ITokenStore tokens,
            IEventHub events,
            ITextHelper textHelper,
            IJsonHelper jsonHelper,
            IAppInfo appInfo)
        {
            _appInfo = appInfo;
            BaseUrl = appInfo.ServiceUrl + "/v2";
            _jsonHelper = jsonHelper;
            _textHelper = textHelper;
            _api = api;
            _accounts = accounts;
            _tokens = tokens;
            _events = events;
        }

        public string BaseUrl { get; set; }

        /// <summary>War der Service bei der letzten Anfrage erreichbar? Sonst "nicht verfügbar", kein Dialog.</summary>
        public bool Available
        {
            get => _available;
            private set
            {
                if (_available == value)
                {
                    return;
                }

                _available = value;
                _events.Publish("service.availability", new { available = value });
            }
        }

        /// <summary>Hat der Server ein Launcher-Update erzwungen (<c>update_required</c>)?</summary>
        public bool UpdateRequired { get; private set; }

        private string tokenKey(string uuid) => "service:" + uuid.Replace("-", "");

        public bool CanUse => _accounts.Active is { Type: AccountType.Microsoft };

        public async Task<T?> Send<T>(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
        {
            using var response = await SendRaw(method, path, body == null ? null : jsonContent(body), cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, _jsonHelper.Options);
        }

        public async Task Send(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
        {
            using var _ = await SendRaw(method, path, body == null ? null : jsonContent(body), cancellationToken);
        }

        public async Task<HttpResponseMessage> SendRaw(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken,
            bool allowAnonymous = false)
        {
            if (!allowAnonymous && !CanUse)
            {
                throw new AxoException("service.notSignedIn");
            }

            for (var attempt = 0; ; attempt++)
            {
                string? token = null;
                if (!allowAnonymous)
                {
                    token = _tokens.GetString(tokenKey(_accounts.Active!.Uuid)) ?? await Register(cancellationToken);
                }

                HttpResponseMessage response;
                try
                {
                    var bytes = content == null ? null : await content.ReadAsByteArrayAsync(cancellationToken);
                    var contentType = content?.Headers.ContentType;
                    response = await _api.Send(() =>
                    {
                        var request = new HttpRequestMessage(method, BaseUrl + path);
                        if (bytes != null)
                        {
                            request.Content = new ByteArrayContent(bytes);
                            request.Content.Headers.ContentType = contentType;
                        }
                        request.Headers.Add("X-AxoClient-Version", _appInfo.Version);
                        return request;
                    }, cancellationToken, request =>
                    {
                        if (token != null)
                        {
                            request.Headers.Authorization = _api.Bearer(token);
                        }
                    });
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    Available = false;
                    throw new AxoException("service.unavailable", inner: exception);
                }
                Available = true;

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && !allowAnonymous)
                {
                    // Token abgelaufen oder widerrufen: still neu anmelden
                    response.Dispose();
                    _tokens.Remove(tokenKey(_accounts.Active!.Uuid));
                    continue;
                }
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                throw await toError(response, cancellationToken);
            }
        }

        private async Task<AxoException> toError(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            string code = "service.error";
            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var codeElement) && codeElement.GetString() is { } value
                    && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
                {
                    code = "service." + value;
                }
            }
            catch (JsonException)
            {
            }
            if (code == "service.update_required")
            {
                UpdateRequired = true;
                _events.Publish("service.updateRequired");
            }
            response.Dispose();
            return new AxoException(code, new() { ["status"] = (int)response.StatusCode });
        }

        /// <summary>Anmeldung beim Service mit einem Mojang-Spielerzertifikat. Gibt das neue Geräte-Token zurück.</summary>
        public async Task<string> Register(CancellationToken cancellationToken)
        {
            await _registerLock.WaitAsync(cancellationToken);
            try
            {
                var account = _accounts.Active ?? throw new AxoException("service.notSignedIn");
                if (_tokens.GetString(tokenKey(account.Uuid)) is { } existing)
                {
                    return existing;
                }

                var session = await _accounts.GetSession(cancellationToken);
                using var certResponse = await _api.Send(() => new HttpRequestMessage(HttpMethod.Post, CertificatesUrl)
                {
                    Content = new ByteArrayContent([]) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }
                }, cancellationToken, request => request.Headers.Authorization = _api.Bearer(session.AccessToken));
                await _api.EnsureSuccess(certResponse, cancellationToken);
                using var cert = JsonDocument.Parse(await certResponse.Content.ReadAsStringAsync(cancellationToken));
                var root = cert.RootElement;
                var keyPair = root.GetProperty("keyPair");

                using var rsa = RSA.Create();
                importPrivateKey(rsa, keyPair.GetProperty("privateKey").GetString()!);
                var uuid = session.CompactUuid;
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var signature = rsa.SignData(Encoding.UTF8.GetBytes($"axoclient:register:{uuid}:{timestamp}"),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                using var response = await _api.Send(() => new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/auth/register")
                {
                    Content = jsonContent(new
                    {
                        uuid,
                        name = session.Name,
                        publicKey = pemBody(keyPair.GetProperty("publicKey").GetString()!),
                        publicKeySignature = root.GetProperty("publicKeySignatureV2").GetString(),
                        expiresAt = DateTimeOffset.Parse(root.GetProperty("expiresAt").GetString()!, CultureInfo.InvariantCulture)
                            .ToUnixTimeMilliseconds(),
                        timestamp,
                        signature = Convert.ToBase64String(signature),
                        device = _textHelper.Clean(Environment.MachineName, 40)
                    })
                }, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw await toError(response, cancellationToken);
                }

                using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var token = answer.RootElement.GetProperty("token").GetString()!;
                _tokens.Set(tokenKey(account.Uuid), token);
                return token;
            }
            catch (HttpRequestException exception)
            {
                Available = false;
                throw new AxoException("service.unavailable", inner: exception);
            }
            finally
            {
                _registerLock.Release();
            }
        }

        /// <summary>Geräte-Token des aktiven Kontos (meldet bei Bedarf neu an), z. B. für den Echtzeit-Kanal.</summary>
        public async Task<string> Token(CancellationToken cancellationToken)
        {
            if (!CanUse)
            {
                throw new AxoException("service.notSignedIn");
            }

            return _tokens.GetString(tokenKey(_accounts.Active!.Uuid)) ?? await Register(cancellationToken);
        }

        /// <summary>Token verwerfen (vom Service abgelehnt); die nächste Anfrage meldet still neu an.</summary>
        public void ForgetToken()
        {
            if (_accounts.Active is { } account)
            {
                _tokens.Remove(tokenKey(account.Uuid));
            }
        }

        /// <summary>Dieses Gerät abmelden (Token beim Service widerrufen und lokal löschen).</summary>
        public async Task SignOutDevice(string uuid, CancellationToken cancellationToken)
        {
            if (_tokens.GetString(tokenKey(uuid)) == null)
            {
                return;
            }

            try
            {
                if (_accounts.Active?.Uuid == uuid)
                {
                    await Send(HttpMethod.Delete, "/auth/token", cancellationToken: cancellationToken);
                }
            }
            catch (AxoException)
            {
            }
            _tokens.Remove(tokenKey(uuid));
        }

        /// <summary>
        /// Mojang beschriftet den Schlüssel als "RSA PRIVATE KEY" (PKCS#1), liefert aber PKCS#8. ImportFromPem glaubt der
        /// Beschriftung und scheitert, deshalb das Format selbst erkennen.
        /// </summary>
        private void importPrivateKey(RSA rsa, string pem)
        {
            var der = Convert.FromBase64String(pemBody(pem));
            try
            {
                rsa.ImportPkcs8PrivateKey(der, out _);
            }
            catch (CryptographicException)
            {
                rsa.ImportRSAPrivateKey(der, out _);
            }
        }

        private string pemBody(string pem) =>
            string.Concat(pem.Split('\n').Select(part => part.Trim()).Where(line => line.Length > 0 && !line.StartsWith("-----")));

        private StringContent jsonContent(object body) =>
            new(JsonSerializer.Serialize(body, _jsonHelper.Compact), Encoding.UTF8, "application/json");
    }
}
