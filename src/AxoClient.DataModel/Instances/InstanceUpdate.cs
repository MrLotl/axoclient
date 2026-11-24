using AxoClient.DataModel.Game;

namespace AxoClient.DataModel.Instances
{
    public sealed record InstanceUpdate(
        string? Name = null,
        string? MinecraftVersion = null,
        LoaderType? Loader = null,
        string? LoaderVersion = null,
        int? MemoryMb = null,
        bool ResetMemory = false,
        string? JavaArgs = null,
        bool? BannerAuto = null,
        bool? AxoMod = null);
}
