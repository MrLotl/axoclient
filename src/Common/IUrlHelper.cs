namespace AxoClient.Common
{
    public interface IUrlHelper
    {
        /// <summary>Nur HTTPS-Adressen durchlassen (Bilder, Links von Modrinth und CurseForge).</summary>
        string? SafeUrl(string? url);
    }
}
