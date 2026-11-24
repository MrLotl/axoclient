using AxoClient.Common;
using AxoClient.DataModel.Game;

namespace AxoClient.DataModel.Import
{
    public sealed record ForeignInstanceView(
        string Id,
        string Name,
        LoaderType? Loader,
        string? MinecraftVersion,
        string? LoaderVersion,
        int Worlds,
        int Mods,
        bool Shared,
        int ModrinthMods,
        TextRef? Hint,
        TextRef? Problem);
}
