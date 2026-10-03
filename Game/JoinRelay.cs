using System.Text.RegularExpressions;

namespace AxoClient.Game;

public static partial class JoinRelay
{
    private const string E4mcProjectId = "qANg5Jrr";

    public static bool IsActiveFor(Installation inst, LauncherSettings settings) =>
        settings.JoinRelayEnabled && inst.Loader == LoaderType.Fabric;

    public static async Task EnsureInstalledAsync(ContentStore store, IProgress<string> status)
    {
        if (store.IsInstalled(E4mcProjectId) || store.HasModMatching("e4mc"))
            return;
        try
        {
            var withFabricApi = !store.HasModMatching("fabric-api", "Fabric API");
            await store.InstallAsync(E4mcProjectId, "e4mc", ContentType.Mod, status, withDependencies: withFabricApi);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("e4mc installieren", ex);
        }
    }

    public static string? DomainFrom(string logLine) =>
        DomainAssigned().Match(logLine) is { Success: true } match ? match.Groups[1].Value : null;

    public static bool IsWorldClosed(string logLine) =>
        logLine.Contains("Stopping server", StringComparison.Ordinal)
        || logLine.Contains("Stopping singleplayer server", StringComparison.Ordinal);

    [GeneratedRegex(@"Domain assigned: ([A-Za-z0-9.\-]+\.[A-Za-z]{2,})")]
    private static partial Regex DomainAssigned();
}
