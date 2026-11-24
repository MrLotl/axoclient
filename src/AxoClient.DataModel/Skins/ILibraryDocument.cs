using AxoClient.Common;

namespace AxoClient.DataModel.Skins
{
    /// <summary>Gemeinsame Form von <c>skins/library.json</c> und <c>capes/library.json</c>.</summary>
    public interface ILibraryDocument<TItem> : IVersionedDocument
    {
        List<TItem> Items { get; }
    }
}
