using System.Net.Http.Headers;

namespace AxoClient.Common
{
    public interface IApiClient
    {
        HttpClient Http { get; }

        Task<T> GetJson<T>(string url, TimeSpan? cacheFor = null, CancellationToken ct = default,
        Action<HttpRequestMessage>? configure = null);

        Task<string> GetString(string url, TimeSpan? cacheFor = null, CancellationToken ct = default,
        Action<HttpRequestMessage>? configure = null);

        Task<TResponse?> PostJson<TResponse>(string url, object body, CancellationToken ct = default,
        Action<HttpRequestMessage>? configure = null);

        /// <summary>Sendet mit Wiederholung bei 429/503 und folgt Weiterleitungen nur zu HTTPS.</summary>
        Task<HttpResponseMessage> Send(Func<HttpRequestMessage> createRequest, CancellationToken ct,
        Action<HttpRequestMessage>? configure = null, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead);

        Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct);

        AuthenticationHeaderValue Bearer(string token);
    }
}
