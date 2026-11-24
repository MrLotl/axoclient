using System.Net;

namespace AxoClient.Common
{
    /// <summary>Legt den HTTP-Handler für alle Anfragen des Launchers an.</summary>
    public class HttpHandlerFactory : IHttpHandlerFactory
    {
        public HttpMessageHandler Create()
        {
            return new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(10),
                // Weiterleitungen prüfen wir selbst, damit nie von HTTPS auf HTTP gewechselt wird
                AllowAutoRedirect = false
            };
        }
    }
}
