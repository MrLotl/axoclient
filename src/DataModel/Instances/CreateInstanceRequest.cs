using AxoClient.DataModel.Game;

namespace AxoClient.DataModel.Instances
{
    public sealed record CreateInstanceRequest(
        string Name,
        string MinecraftVersion,
        LoaderType Loader,
        string? LoaderVersion,
        int? MemoryMb = null,
        string? CopyFrom = null,
        string? ArtSeed = null);
}
