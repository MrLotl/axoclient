using AxoClient.DataModel.Game;

namespace AxoClient.DataModel.Import
{
    /// <summary>Was die UI für eine ausgewählte Instanz schickt: Name, Loader und Version dürfen geändert werden.</summary>
    public sealed record ForeignImportChoice(string Id, string Name, LoaderType Loader, string MinecraftVersion, ForeignImportParts Parts);
}
