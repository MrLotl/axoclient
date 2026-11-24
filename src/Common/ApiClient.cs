using System.IO.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AxoClient.Common
{
    /// <summary>
    /// Die einzige Stelle für HTTP: eigener User-Agent, nur HTTPS, Wiederholung bei 429/503 mit
    /// <c>Retry-After</c>, Zeitlimits und ein Datei-Cache in <c>cache/api</c>. Fällt ein Dienst aus,
    /// wird aus dem Cache weitergearbeitet.
    /// </summary>
    public sealed class ApiClient : IApiClient
    {
        private readonly IFileSystem _fs;
        private readonly string _cacheDir;
        private readonly ITextHelper _textHelper;
        private readonly IJsonHelper _jsonHelper;
        private readonly IAppLog _appLog;
        private readonly ILogRedactor _logRedactor;
        public const string UserAgent = "AxoClient/1.0 (github.com/MrLotl)";
        private const int MaxAttempts = 4;

        private readonly TimeProvider _time;

        public ApiClient(
            HttpMessageHandler handler,
            IFileSystem fs,
            string cacheDir,
            ITextHelper textHelper,
            IJsonHelper jsonHelper,
            IAppLog appLog,
            ILogRedactor logRedactor,
            TimeProvider? time = null)
        {
            _fs = fs;
            _cacheDir = cacheDir;
            _textHelper = textHelper;
            _jsonHelper = jsonHelper;
            _appLog = appLog;
            _logRedactor = logRedactor;
            _time = time ?? TimeProvider.System;
            Http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
            Http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        public HttpClient Http { get; }

        /// <summary>Für Tests: Verzögerungen bei Wiederholungen überspringen.</summary>
        internal bool NoRetryDelay { get; set; }

        public async Task<T> GetJson<T>(string url, TimeSpan? cacheFor = null, CancellationToken ct = default,
            Action<HttpRequestMessage>? configure = null)
        {
            var text = await GetString(url, cacheFor, ct, configure);
            return JsonSerializer.Deserialize<T>(text, _jsonHelper.Options)
                   ?? throw new AxoException("errors.badResponse", new() { ["host"] = new Uri(url).Host });
        }

        public async Task<string> GetString(string url, TimeSpan? cacheFor = null, CancellationToken ct = default,
            Action<HttpRequestMessage>? configure = null)
        {
            var cacheFile = cacheFor.HasValue ? getCacheFile(url) : null;
            if (cacheFile != null && _fs.File.Exists(cacheFile)
                                  && _time.GetUtcNow() - _fs.File.GetLastWriteTimeUtc(cacheFile) < cacheFor!.Value)
            {
                return await _fs.File.ReadAllTextAsync(cacheFile, ct);
            }

            try
            {
                using var response = await Send(() => new HttpRequestMessage(HttpMethod.Get, url), ct, configure);
                await EnsureSuccess(response, ct);
                var text = await response.Content.ReadAsStringAsync(ct);
                if (cacheFile != null)
                {
                    _fs.Directory.CreateDirectory(_cacheDir);
                    await _fs.File.WriteAllTextAsync(cacheFile, text, ct);
                }
                return text;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // Offline oder Dienst gestört: lieber alte Daten als gar keine
                if (cacheFile != null && _fs.File.Exists(cacheFile))
                {
                    _appLog.Warn($"{new Uri(url).Host} nicht erreichbar, nutze Cache");
                    return await _fs.File.ReadAllTextAsync(cacheFile, ct);
                }
                throw;
            }
        }

        public async Task<TResponse?> PostJson<TResponse>(string url, object body, CancellationToken ct = default,
            Action<HttpRequestMessage>? configure = null)
        {
            var json = JsonSerializer.Serialize(body, _jsonHelper.Compact);
            using var response = await Send(() => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }, ct, configure);
            await EnsureSuccess(response, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<TResponse>(text, _jsonHelper.Options);
        }

        /// <summary>Sendet mit Wiederholung bei 429/503 und folgt Weiterleitungen nur zu HTTPS.</summary>
        public async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> createRequest, CancellationToken ct,
            Action<HttpRequestMessage>? configure = null, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
        {
            for (var attempt = 1; ; attempt++)
            {
                var request = createRequest();
                requireHttps(request.RequestUri!);
                configure?.Invoke(request);

                HttpResponseMessage response;
                try
                {
                    response = await sendFollowingRedirects(request, completion, ct);
                }
                catch (HttpRequestException) when (attempt < MaxAttempts)
                {
                    await delay(TimeSpan.FromSeconds(attempt), ct);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                    && attempt < MaxAttempts)
                {
                    var wait = response.Headers.RetryAfter?.Delta
                               ?? (response.Headers.RetryAfter?.Date is { } date ? date - _time.GetUtcNow() : (TimeSpan?)null)
                               ?? TimeSpan.FromSeconds(2 * attempt);
                    response.Dispose();
                    await delay(wait > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : wait, ct);
                    continue;
                }
                return response;
            }
        }

        private async Task<HttpResponseMessage> sendFollowingRedirects(HttpRequestMessage request,
            HttpCompletionOption completion, CancellationToken ct)
        {
            for (var hops = 0; hops < 8; hops++)
            {
                var response = await Http.SendAsync(request, completion, ct);
                if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is not { } location)
                {
                    return response;
                }

                var next = location.IsAbsoluteUri ? location : new Uri(request.RequestUri!, location);
                requireHttps(next);
                response.Dispose();
                var follow = new HttpRequestMessage(request.Method == HttpMethod.Head ? HttpMethod.Head : HttpMethod.Get, next);
                foreach (var header in request.Headers.Where(h => h.Key is not "Authorization" and not "x-api-key"))
                {
                    follow.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                request.Dispose();
                request = follow;
            }
            throw new AxoException("errors.tooManyRedirects");
        }

        private void requireHttps(Uri uri)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            {
                throw new AxoException("errors.insecureUrl", new() { ["host"] = uri.Host });
            }
        }

        public async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var host = response.RequestMessage?.RequestUri?.Host ?? "";
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new AxoException("errors.notFound", new() { ["host"] = host });
            }

            var body = response.Content.Headers.ContentLength is < 4096
                ? await response.Content.ReadAsStringAsync(ct)
                : "";
            throw new HttpRequestException(_logRedactor.Redact($"{host} antwortet mit {(int)response.StatusCode}: {body}"),
                null, response.StatusCode);
        }

        private Task delay(TimeSpan wait, CancellationToken ct) =>
            NoRetryDelay ? Task.CompletedTask : Task.Delay(wait, ct);

        private string getCacheFile(string url) =>
            _fs.Path.Combine(_cacheDir, _textHelper.Sha256Hex(Encoding.UTF8.GetBytes(url))[..32] + ".json");

        public AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);
    }
}
