using System.Reflection;

namespace AxoClient.Core;

public static class AppInfo
{
    public const string Name = "AxoClient";
    public const string UserAgent = "AxoClient/1.0";
    public const string DevVersion = "0.0.0-dev";

    public const string AxoServiceUrl = "https://mclauncher-badge.bernhardtfinn0.workers.dev";
    public const string ClientCapesUrl = AxoServiceUrl + "/capes/";
    public const string DiscordAppId = "1550925254124118067";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? DevVersion;

    public static string? UpdateRepo { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdateRepo")?.Value is { Length: > 0 } repo ? repo : null;
}
