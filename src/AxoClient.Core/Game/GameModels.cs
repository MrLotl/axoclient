using System.Text.Json.Serialization;

namespace AxoClient.Core.Game;

public enum LoaderType
{
    Vanilla,
    Fabric,
    Forge,
    [JsonStringEnumMemberName("neoforge")] NeoForge,
    Quilt
}

public enum VersionType
{
    Release,
    Snapshot,
    Old
}

public sealed record MinecraftVersionRef(string Version, VersionType Type);

public sealed record LoaderRef(LoaderType Type, string? Version);

public sealed record GameVersionInfo(string Id, VersionType Type, DateTimeOffset ReleaseTime);

public sealed record LoaderVersionInfo(string Version, bool Stable, bool Recommended);

public static class LoaderNames
{
    public static string Display(LoaderType type) => type switch
    {
        LoaderType.Vanilla => "Vanilla",
        LoaderType.Fabric => "Fabric",
        LoaderType.Forge => "Forge",
        LoaderType.NeoForge => "NeoForge",
        LoaderType.Quilt => "Quilt",
        _ => type.ToString()
    };

    /// <summary>Bezeichnung wie bei Modrinth (<c>loaders</c>-Filter).</summary>
    public static string ModrinthId(LoaderType type) => type switch
    {
        LoaderType.NeoForge => "neoforge",
        _ => type.ToString().ToLowerInvariant()
    };

    public static bool SupportsMods(LoaderType type) => type != LoaderType.Vanilla;
}
