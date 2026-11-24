namespace AxoClient.Common
{
    public interface IAppInfo
    {
        /// <summary>Online-Service (Cloudflare Worker). Für die Entwicklung per <c>AXOCLIENT_API</c> überschreibbar.</summary>
        string ServiceUrl { get; }

        string Version { get; }

        /// <summary>Kurz für die Titelleiste: "1.0".</summary>
        string ShortVersion { get; }
    }
}
