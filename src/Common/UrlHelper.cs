namespace AxoClient.Common
{
    /// <summary>Prüft Adressen aus fremden Quellen.</summary>
    public class UrlHelper : IUrlHelper
    {
        /// <summary>Nur HTTPS-Adressen durchlassen (Bilder, Links von Modrinth und CurseForge).</summary>
        public string? SafeUrl(string? url)
        {
            if (url != null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                return url;
            }
            return null;
        }
    }
}
