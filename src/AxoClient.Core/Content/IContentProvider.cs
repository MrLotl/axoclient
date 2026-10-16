using AxoClient.Core.Game;

namespace AxoClient.Core.Content;

public enum ProjectKind
{
    Mod,
    ResourcePack,
    ShaderPack,
    Modpack
}

public enum SearchSort
{
    Popular,
    Relevance,
    Newest,
    Updated
}

public sealed record SearchQuery(
    ProjectKind Kind,
    string Text,
    string? MinecraftVersion,
    LoaderType? Loader,
    string? Category,
    SearchSort Sort,
    int Page,
    int PageSize = 20);

public sealed record ContentProject(
    string Source,
    string Id,
    string Slug,
    string Title,
    string Author,
    string Description,
    string? IconUrl,
    string? ImageUrl,
    long Downloads,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<string> GameVersions,
    DateTimeOffset? Updated,
    string WebUrl);

public sealed record SearchPage(IReadOnlyList<ContentProject> Items, int Total);

public enum DependencyKind
{
    Required,
    Optional,
    Incompatible,
    Embedded
}

public sealed record ContentDependency(string? ProjectId, string? VersionId, DependencyKind Kind);

public sealed record ContentFile(string Url, string FileName, string? Sha1, string? Sha512, long Size, bool Primary);

public sealed record ContentVersion(
    string Source,
    string Id,
    string ProjectId,
    string Name,
    string VersionNumber,
    string Channel,
    DateTimeOffset Published,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<ContentFile> Files,
    IReadOnlyList<ContentDependency> Dependencies,
    string? Changelog = null)
{
    public ContentFile? PrimaryFile => Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
}

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

    Task<SearchPage> SearchAsync(SearchQuery query, CancellationToken ct);

    Task<ContentProject> GetProjectAsync(string id, CancellationToken ct);

    Task<IReadOnlyDictionary<string, ContentProject>> GetProjectsAsync(IEnumerable<string> ids, CancellationToken ct);

    /// <summary>Versionen eines Projekts, optional auf MC-Version und Loader gefiltert. Neueste zuerst.</summary>
    Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(string projectId, string? minecraftVersion, LoaderType? loader,
        ProjectKind kind, CancellationToken ct);

    Task<ContentVersion?> GetVersionAsync(string versionId, CancellationToken ct);

    /// <summary>Dateien anhand ihres SHA-1 erkennen (viele auf einmal).</summary>
    Task<IReadOnlyDictionary<string, ContentVersion>> IdentifyAsync(IReadOnlyCollection<LocalFileHash> files, CancellationToken ct);
}

/// <summary>Hash einer lokalen Datei; CurseForge braucht zusätzlich den Murmur2-Fingerprint.</summary>
public sealed record LocalFileHash(string Sha1, uint? Fingerprint);

public sealed class ContentProviders(IEnumerable<IContentProvider> providers)
{
    private readonly Dictionary<string, IContentProvider> _byName = providers.ToDictionary(p => p.Source);

    public IReadOnlyCollection<IContentProvider> All => _byName.Values;

    public IContentProvider Get(string source) =>
        _byName.TryGetValue(source, out var p) && p.IsAvailable
            ? p
            : throw new Common.AxoException("content.sourceUnavailable", new() { ["source"] = source });

    public IContentProvider Get(ContentSource source) => Get(source == ContentSource.CurseForge ? "curseforge" : "modrinth");

    public bool IsAvailable(string source) => _byName.TryGetValue(source, out var p) && p.IsAvailable;
}
