using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;

namespace AxoClient.Content.Contract
{
    public interface IModrinthProvider : IContentProvider
    {
        string ProjectType(ProjectKind kind);

        /// <summary>
        /// <c>server_side</c> je Projekt (<c>required</c>, <c>optional</c>, <c>unsupported</c>, <c>unknown</c>): beim Nachjoinen in eine
        /// offene Welt zählen reine Client-Mods des Gastgebers nicht als „fehlt“.
        /// </summary>
        Task<IReadOnlyDictionary<string, string>> ServerSides(IEnumerable<string> ids, CancellationToken ct);

        Task<IReadOnlyDictionary<string, ContentVersion>> GetVersionsByIds(IEnumerable<string> ids, CancellationToken ct);

        /// <summary>Neueste passende Version für viele Dateien auf einmal (Update-Prüfung).</summary>
        Task<IReadOnlyDictionary<string, ContentVersion>> LatestForHashes(IReadOnlyCollection<string> sha1,
        string minecraftVersion, LoaderType? loader, ProjectKind kind, CancellationToken ct);

        string[] LoaderCategories(LoaderType loader);
    }
}
