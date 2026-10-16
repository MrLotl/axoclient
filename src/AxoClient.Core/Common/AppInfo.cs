using System.Reflection;

namespace AxoClient.Core.Common;

public static class AppInfo
{
    public const string Name = "AxoClient";

    /// <summary>GitHub-Repo für Releases (Velopack) und Links.</summary>
    public const string Repository = "https://github.com/MrLotl/AxoClient";

    /// <summary>Online-Service (Cloudflare Worker). Für die Entwicklung per <c>AXOCLIENT_API</c> überschreibbar.</summary>
    public static string ServiceUrl { get; } =
        (Environment.GetEnvironmentVariable("AXOCLIENT_API") is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out _)
            ? url
            : "https://axoclient-api.bernhardtfinn0.workers.dev").TrimEnd('/');

    /// <summary>Erst einschalten, wenn der CurseForge-API-Key im Worker hinterlegt ist.</summary>
    public const bool CurseForgeEnabled = false;

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    /// <summary>Kurz für die Titelleiste: "1.0".</summary>
    public static string ShortVersion
    {
        get
        {
            var core = Version.Split('-')[0].Split('.');
            return core.Length >= 2 ? $"{core[0]}.{core[1]}" : Version;
        }
    }
}
