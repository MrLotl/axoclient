using System.Text.Json;
using AxoClient.Core.Common;
using AxoClient.Core.Game;

namespace AxoClient.Core.Content;

/// <summary>Modrinth API v2 (ohne Key, 300 Anfragen pro Minute; Wiederholung bei 429 im <see cref="ApiClient"/>).</summary>
public sealed class ModrinthProvider(ApiClient api) : IContentProvider
{
    private const string Api = "https://api.modrinth.com/v2";
    private const int IdsPerRequest = 50;

    private static readonly TimeSpan SearchCache = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VersionCache = TimeSpan.FromHours(1);

    public string Source => "modrinth";
    public bool IsAvailable => true;

    public static string ProjectType(ProjectKind kind) => kind switch
    {
        ProjectKind.Mod => "mod",
        ProjectKind.ResourcePack => "resourcepack",
        ProjectKind.ShaderPack => "shader",
        _ => "modpack"
    };

    public async Task<SearchPage> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        var projectType = ProjectType(query.Kind);
        var facets = new List<string[]> { new[] { $"project_type:{projectType}" } };
        if (query.MinecraftVersion != null)
            facets.Add([$"versions:{query.MinecraftVersion}"]);
        if (query.Loader is { } loader && loader != LoaderType.Vanilla && query.Kind is ProjectKind.Mod or ProjectKind.Modpack)
            facets.Add(LoaderCategories(loader).Select(c => $"categories:{c}").ToArray());
        else if (query.Kind == ProjectKind.Modpack)
            facets.Add(["categories:fabric", "categories:forge", "categories:neoforge", "categories:quilt"]);
        if (!string.IsNullOrWhiteSpace(query.Category) && Text.IsValidId(query.Category))
            facets.Add([$"categories:{query.Category}"]);

        var index = query.Sort switch
        {
            SearchSort.Newest => "newest",
            SearchSort.Updated => "updated",
            SearchSort.Relevance when !string.IsNullOrWhiteSpace(query.Text) => "relevance",
            _ => "downloads"
        };
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var url = $"{Api}/search?limit={pageSize}&offset={Math.Max(0, query.Page) * pageSize}&index={index}" +
                  $"&query={Uri.EscapeDataString(Text.Clean(query.Text, 100))}" +
                  $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";

        using var json = JsonDocument.Parse(await api.GetStringAsync(url, SearchCache, ct));
        var items = json.RootElement.GetProperty("hits").EnumerateArray().Select(hit =>
        {
            var categories = Strings(hit, "display_categories");
            if (categories.Count == 0)
                categories = Strings(hit, "categories");
            var slug = hit.GetProperty("slug").GetString() ?? "";
            return new ContentProject(Source,
                hit.GetProperty("project_id").GetString()!,
                slug,
                Text.Clean(hit.GetProperty("title").GetString(), 100),
                Text.Clean(Str(hit, "author"), 60),
                Text.Clean(Str(hit, "description"), 400),
                SafeUrl(Str(hit, "icon_url")),
                SafeUrl(hit.TryGetProperty("featured_gallery", out var g) && g.ValueKind == JsonValueKind.String
                    ? g.GetString()
                    : Strings(hit, "gallery").FirstOrDefault()),
                hit.TryGetProperty("downloads", out var d) ? d.GetInt64() : 0,
                categories.Where(c => !IsLoaderCategory(c)).ToList(),
                Strings(hit, "categories").Where(IsLoaderCategory).ToList(),
                Strings(hit, "versions"),
                hit.TryGetProperty("date_modified", out var m) && m.ValueKind == JsonValueKind.String ? m.GetDateTimeOffset() : null,
                $"https://modrinth.com/{projectType}/{Uri.EscapeDataString(slug)}");
        }).ToList();
        return new SearchPage(items, json.RootElement.GetProperty("total_hits").GetInt32());
    }

    public async Task<ContentProject> GetProjectAsync(string id, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await api.GetStringAsync($"{Api}/project/{Uri.EscapeDataString(id)}", VersionCache, ct));
        return ParseProject(json.RootElement);
    }

    public async Task<IReadOnlyDictionary<string, ContentProject>> GetProjectsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var result = new Dictionary<string, ContentProject>();
        foreach (var chunk in ids.Distinct().Chunk(IdsPerRequest))
        {
            using var json = JsonDocument.Parse(await api.GetStringAsync(
                $"{Api}/projects?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}", VersionCache, ct));
            foreach (var project in json.RootElement.EnumerateArray().Select(ParseProject))
                result[project.Id] = project;
        }
        return result;
    }

    /// <summary>
    /// <c>server_side</c> je Projekt (<c>required</c>, <c>optional</c>, <c>unsupported</c>, <c>unknown</c>): beim Nachjoinen in eine
    /// offene Welt zählen reine Client-Mods des Gastgebers nicht als „fehlt“.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ServerSidesAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var result = new Dictionary<string, string>();
        foreach (var chunk in ids.Distinct().Chunk(IdsPerRequest))
        {
            using var json = JsonDocument.Parse(await api.GetStringAsync(
                $"{Api}/projects?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}", VersionCache, ct));
            foreach (var p in json.RootElement.EnumerateArray())
                if (p.TryGetProperty("id", out var id) && id.GetString() is { } key)
                    result[key] = Str(p, "server_side") ?? "unknown";
        }
        return result;
    }

    private ContentProject ParseProject(JsonElement p)
    {
        var type = Str(p, "project_type") ?? "mod";
        var slug = Str(p, "slug") ?? "";
        return new ContentProject(Source, p.GetProperty("id").GetString()!, slug,
            Text.Clean(Str(p, "title"), 100), "", Text.Clean(Str(p, "description"), 400), SafeUrl(Str(p, "icon_url")),
            null, p.TryGetProperty("downloads", out var d) ? d.GetInt64() : 0,
            Strings(p, "categories").Where(c => !IsLoaderCategory(c)).ToList(),
            Strings(p, "loaders"), Strings(p, "game_versions"),
            p.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.String ? u.GetDateTimeOffset() : null,
            $"https://modrinth.com/{type}/{Uri.EscapeDataString(slug)}");
    }

    public async Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(string projectId, string? minecraftVersion,
        LoaderType? loader, ProjectKind kind, CancellationToken ct)
    {
        var url = $"{Api}/project/{Uri.EscapeDataString(projectId)}/version?include_changelog=false";
        if (minecraftVersion != null)
            url += $"&game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion }))}";
        if (loader is { } l && l != LoaderType.Vanilla && kind is ProjectKind.Mod or ProjectKind.Modpack)
            url += $"&loaders={Uri.EscapeDataString(JsonSerializer.Serialize(LoaderCategories(l)))}";
        else if (kind == ProjectKind.ShaderPack)
            url += $"&loaders={Uri.EscapeDataString("[\"iris\",\"optifine\",\"canvas\",\"vanilla\"]")}";
        using var json = JsonDocument.Parse(await api.GetStringAsync(url, VersionCache, ct));
        return json.RootElement.EnumerateArray().Select(ParseVersion).OrderByDescending(v => v.Published).ToList();
    }

    public async Task<ContentVersion?> GetVersionAsync(string versionId, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await api.GetStringAsync($"{Api}/version/{Uri.EscapeDataString(versionId)}",
                VersionCache, ct));
            return ParseVersion(json.RootElement);
        }
        catch (AxoException ex) when (ex.Code == "errors.notFound")
        {
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<string, ContentVersion>> GetVersionsByIdsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var result = new Dictionary<string, ContentVersion>();
        foreach (var chunk in ids.Distinct().Chunk(IdsPerRequest))
        {
            using var json = JsonDocument.Parse(await api.GetStringAsync(
                $"{Api}/versions?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}", VersionCache, ct));
            foreach (var version in json.RootElement.EnumerateArray().Select(ParseVersion))
                result[version.Id] = version;
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<string, ContentVersion>> IdentifyAsync(IReadOnlyCollection<LocalFileHash> files,
        CancellationToken ct)
    {
        var hashes = files.Select(f => f.Sha1).Distinct().ToList();
        if (hashes.Count == 0)
            return new Dictionary<string, ContentVersion>();
        var result = new Dictionary<string, ContentVersion>();
        foreach (var chunk in hashes.Chunk(500))
        {
            var response = await api.PostJsonAsync<Dictionary<string, JsonElement>>($"{Api}/version_files",
                new { hashes = chunk, algorithm = "sha1" }, ct);
            foreach (var (hash, version) in response ?? [])
                result[hash] = ParseVersion(version);
        }
        return result;
    }

    /// <summary>Neueste passende Version für viele Dateien auf einmal (Update-Prüfung).</summary>
    public async Task<IReadOnlyDictionary<string, ContentVersion>> LatestForHashesAsync(IReadOnlyCollection<string> sha1,
        string minecraftVersion, LoaderType? loader, ProjectKind kind, CancellationToken ct)
    {
        if (sha1.Count == 0)
            return new Dictionary<string, ContentVersion>();
        var loaders = loader is { } l && l != LoaderType.Vanilla && kind == ProjectKind.Mod
            ? LoaderCategories(l)
            : kind == ProjectKind.ShaderPack ? ["iris", "optifine"] : kind == ProjectKind.ResourcePack ? ["minecraft"] : [];
        var result = new Dictionary<string, ContentVersion>();
        foreach (var chunk in sha1.Distinct().Chunk(500))
        {
            object body = loaders.Length > 0
                ? new { hashes = chunk, algorithm = "sha1", loaders, game_versions = new[] { minecraftVersion } }
                : new { hashes = chunk, algorithm = "sha1", game_versions = new[] { minecraftVersion } };
            var response = await api.PostJsonAsync<Dictionary<string, JsonElement>>($"{Api}/version_files/update", body, ct);
            foreach (var (hash, version) in response ?? [])
                result[hash] = ParseVersion(version);
        }
        return result;
    }

    internal ContentVersion ParseVersion(JsonElement v)
    {
        var files = v.GetProperty("files").EnumerateArray().Select(f =>
        {
            var hashes = f.GetProperty("hashes");
            return new ContentFile(f.GetProperty("url").GetString()!, f.GetProperty("filename").GetString()!,
                hashes.TryGetProperty("sha1", out var s1) ? s1.GetString() : null,
                hashes.TryGetProperty("sha512", out var s5) ? s5.GetString() : null,
                f.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
                f.TryGetProperty("primary", out var primary) && primary.GetBoolean());
        }).ToList();

        var deps = v.TryGetProperty("dependencies", out var d)
            ? d.EnumerateArray().Select(dep => new ContentDependency(
                Str(dep, "project_id"), Str(dep, "version_id"),
                Str(dep, "dependency_type") switch
                {
                    "required" => DependencyKind.Required,
                    "incompatible" => DependencyKind.Incompatible,
                    "embedded" => DependencyKind.Embedded,
                    _ => DependencyKind.Optional
                })).ToList()
            : [];

        return new ContentVersion(Source, v.GetProperty("id").GetString()!, v.GetProperty("project_id").GetString()!,
            Text.Clean(Str(v, "name"), 120), Text.Clean(Str(v, "version_number"), 80), Str(v, "version_type") ?? "release",
            v.GetProperty("date_published").GetDateTimeOffset(), Strings(v, "game_versions"),
            Strings(v, "loaders").Select(x => x.ToLowerInvariant()).ToList(), files, deps,
            Text.CleanMultiline(Str(v, "changelog"), 20_000) is { Length: > 0 } c ? c : null);
    }

    public static string[] LoaderCategories(LoaderType loader) => loader switch
    {
        // Quilt lädt auch Fabric-Mods
        LoaderType.Quilt => ["quilt", "fabric"],
        _ => [LoaderNames.ModrinthId(loader)]
    };

    private static bool IsLoaderCategory(string c) =>
        c is "fabric" or "forge" or "neoforge" or "quilt" or "liteloader" or "modloader" or "rift" or "iris" or "optifine"
            or "canvas" or "vanilla" or "minecraft" or "bukkit" or "paper" or "spigot" or "velocity" or "datapack";

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];

    /// <summary>Nur HTTPS-Bilder von bekannten Hosts durchlassen.</summary>
    internal static string? SafeUrl(string? url) =>
        url != null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? url : null;
}
