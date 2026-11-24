using AxoClient.DataModel.Game;

namespace AxoClient.DataModel.Instances
{
    /// <summary>Was die Instanzliste der UI braucht.</summary>
    public sealed record InstanceSummary(
        string Id,
        string Name,
        string MinecraftVersion,
        LoaderType Loader,
        string? LoaderVersion,
        string IconUrl,
        string? BannerUrl,
        bool HasIcon,
        DateTimeOffset? LastPlayed,
        long TotalSeconds,
        bool Running,
        int ModCount,
        DateTimeOffset CreatedAt,
        ModLockLevel ModLock,
        string ArtSeed,
        bool GradientOnly,
        int PanoramaView);
}
