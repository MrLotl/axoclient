using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;

namespace AxoClient.Content.Contract
{
    /// <summary>
    /// Eine Inhaltsquelle (Modrinth, CurseForge). Beide liefern dieselben Modelle, der Rest des Launchers kennt
    /// den Unterschied nicht.
    /// </summary>
    public interface IContentProvider
    {
        /// <summary><c>modrinth</c> oder <c>curseforge</c>.</summary>
        string Source { get; }

        /// <summary>CurseForge ist ohne API-Key nicht verfügbar ("Bald verfügbar").</summary>
        bool IsAvailable { get; }

        Task<SearchPage> Search(SearchQuery query, CancellationToken cancellationToken);

        Task<ContentProject> GetProject(string id, CancellationToken cancellationToken);

        Task<IReadOnlyDictionary<string, ContentProject>> GetProjects(IEnumerable<string> ids, CancellationToken cancellationToken);

        /// <summary>Versionen eines Projekts, optional auf MC-Version und Loader gefiltert. Neueste zuerst.</summary>
        Task<IReadOnlyList<ContentVersion>> GetVersions(string projectId, string? minecraftVersion, LoaderType? loader,
            ProjectKind kind, CancellationToken cancellationToken);

        Task<ContentVersion?> GetVersion(string versionId, CancellationToken cancellationToken);

        /// <summary>Dateien anhand ihres SHA-1 erkennen (viele auf einmal).</summary>
        Task<IReadOnlyDictionary<string, ContentVersion>> Identify(IReadOnlyCollection<LocalFileHash> files, CancellationToken cancellationToken);
    }
}
