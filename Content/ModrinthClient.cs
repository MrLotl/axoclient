using System.Text;
using System.Text.Json;

namespace AxoClient.Content;

public class ModrinthClient(HttpClient http)
{
    private const string Api = "https://api.modrinth.com/v2";
    private const int IdsPerRequest = 50;

    public HttpClient Http => http;

    public async Task<SearchPage> SearchAsync(string query, ContentType type, Installation inst, int page, int pageSize,
        string? category = null, string? index = null)
    {
        var projectType = type switch
        {
            ContentType.Mod => "mod",
            ContentType.ResourcePack => "resourcepack",
            _ => "shader"
        };
        var facets = new List<string[]>
        {
            new[] { $"project_type:{projectType}" },
            new[] { $"versions:{inst.MinecraftVersion}" }
        };
        if (type == ContentType.Mod)
            facets.Add(inst.Loader.ModrinthNames().Select(name => $"categories:{name}").ToArray());
        if (category != null)
            facets.Add([$"categories:{category}"]);
        return await SearchAsync(query, facets, page, pageSize, projectType, CategoryTags, index);
    }

    public Task<SearchPage> SearchModpacksAsync(string query, int page, int pageSize, string? loader = null,
        string? minecraft = null, string? index = null)
    {
        var facets = new List<string[]>
        {
            new[] { "project_type:modpack" },
            loader != null
                ? new[] { $"categories:{loader}" }
                : new[] { "categories:fabric", "categories:forge", "categories:neoforge", "categories:quilt" }
        };
        if (minecraft != null)
            facets.Add([$"versions:{minecraft}"]);
        return SearchAsync(query, facets, page, pageSize, "modpack", ModpackTags, index);
    }

    private async Task<SearchPage> SearchAsync(string query, List<string[]> facets, int page, int pageSize,
        string projectType, Func<JsonElement, string> tags, string? index = null)
    {
        var url = $"{Api}/search?limit={pageSize}&offset={page * pageSize}" +
                  $"&index={index ?? (string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance")}" +
                  $"&query={Uri.EscapeDataString(query)}" +
                  $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";

        using var json = await GetJsonAsync(url);
        var items = json.RootElement.GetProperty("hits").EnumerateArray().Select(hit => new ContentProject
        {
            Id = hit.GetProperty("project_id").GetString()!,
            Title = hit.GetProperty("title").GetString()!,
            Author = hit.GetProperty("author").GetString() ?? "",
            Description = hit.GetProperty("description").GetString() ?? "",
            IconUrl = NullIfEmpty(hit.GetProperty("icon_url").GetString()),
            Downloads = hit.GetProperty("downloads").GetInt64(),
            WebsiteUrl = $"https://modrinth.com/{projectType}/{hit.GetProperty("slug").GetString()}",
            Tags = tags(hit),
            ImageUrl = NullIfEmpty(hit.TryGetProperty("featured_gallery", out var gallery) && gallery.ValueKind == JsonValueKind.String
                ? gallery.GetString()
                : hit.TryGetProperty("gallery", out var images) && images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0
                    ? images[0].GetString()
                    : null),
            Updated = hit.TryGetProperty("date_modified", out var modified) && modified.ValueKind == JsonValueKind.String
                && DateTime.TryParse(modified.GetString(), out var updated) ? updated : null
        }).ToList();
        return new SearchPage(items, json.RootElement.GetProperty("total_hits").GetInt32());
    }

    private static readonly Dictionary<string, string> CategoryNames = new()
    {
        ["optimization"] = "Leistung", ["utility"] = "Komfort", ["library"] = "Bibliothek", ["technology"] = "Technik",
        ["adventure"] = "Abenteuer", ["decoration"] = "Deko", ["storage"] = "Lager", ["worldgen"] = "Welt",
        ["magic"] = "Magie", ["management"] = "Verwaltung", ["social"] = "Sozial", ["vanilla-like"] = "Vanilla-Stil",
        ["realistic"] = "Realistisch", ["gui"] = "Oberfläche", ["fantasy"] = "Fantasy", ["semi-realistic"] = "Halb-realistisch",
        ["cartoon"] = "Cartoon", ["simplistic"] = "Schlicht", ["low"] = "Leistung", ["potato"] = "Sehr schnell",
        ["equipment"] = "Ausrüstung", ["blocks"] = "Blöcke", ["items"] = "Items", ["models"] = "Modelle",
        ["tweaks"] = "Anpassungen", ["combat"] = "Kampf", ["food"] = "Essen", ["mobs"] = "Mobs", ["transportation"] = "Transport"
    };

    private static string CategoryTags(JsonElement hit) =>
        hit.TryGetProperty("display_categories", out var categories) || hit.TryGetProperty("categories", out categories)
            ? string.Join(", ", categories.EnumerateArray().Select(c => c.GetString() ?? "")
                .Where(CategoryNames.ContainsKey).Select(c => CategoryNames[c]).Distinct().Take(2))
            : "";

    private static string ModpackTags(JsonElement hit)
    {
        var loaders = hit.TryGetProperty("categories", out var categories)
            ? categories.EnumerateArray().Select(c => c.GetString()).Where(c => c is "fabric" or "forge" or "neoforge" or "quilt")
                .Select(c => c switch { "fabric" => "Fabric", "forge" => "Forge", "neoforge" => "NeoForge", _ => "Quilt" }).Distinct().ToList()
            : [];
        var newest = hit.TryGetProperty("versions", out var versions) && versions.GetArrayLength() > 0
            ? versions[versions.GetArrayLength() - 1].GetString()
            : null;
        return string.Join(" · ", new[] { newest ?? "", string.Join("/", loaders) }.Where(s => s.Length > 0));
    }

    public async Task<List<ContentVersion>> GetProjectVersionsAsync(string projectId)
    {
        using var json = await GetJsonAsync($"{Api}/project/{Uri.EscapeDataString(projectId)}/version");
        return ParseVersions(json.RootElement);
    }

    public async Task<List<ContentVersion>> GetVersionsAsync(string projectId, ContentType type, Installation inst)
    {
        var url = $"{Api}/project/{projectId}/version" +
                  $"?game_versions={Uri.EscapeDataString($"[\"{inst.MinecraftVersion}\"]")}";
        if (type == ContentType.Mod)
            url += $"&loaders={Uri.EscapeDataString("[" + string.Join(",", inst.Loader.ModrinthNames().Select(name => $"\"{name}\"")) + "]")}";
        using var json = await GetJsonAsync(url);
        return ParseVersions(json.RootElement);
    }

    public async Task<ContentVersion?> GetLatestVersionAsync(string projectId, ContentType type, Installation inst) =>
        ContentVersion.Newest(await GetVersionsAsync(projectId, type, inst));

    public async Task<ContentVersion?> GetVersionAsync(string versionId)
    {
        try
        {
            using var json = await GetJsonAsync($"{Api}/version/{versionId}");
            return ParseVersion(json.RootElement);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<Dictionary<string, ContentVersion>> GetVersionsByIdsAsync(IEnumerable<string> versionIds)
    {
        var result = new Dictionary<string, ContentVersion>();
        foreach (var chunk in versionIds.Distinct().Chunk(IdsPerRequest))
        {
            using var json = await GetJsonAsync($"{Api}/versions?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}");
            foreach (var version in json.RootElement.EnumerateArray().Select(ParseVersion))
                result[version.Id] = version;
        }
        return result;
    }

    public async Task<Dictionary<string, ProjectSummary>> GetProjectsAsync(IEnumerable<string> projectIds)
    {
        var result = new Dictionary<string, ProjectSummary>();
        foreach (var chunk in projectIds.Distinct().Chunk(IdsPerRequest))
        {
            using var json = await GetJsonAsync($"{Api}/projects?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}");
            foreach (var project in json.RootElement.EnumerateArray())
            {
                var id = project.GetProperty("id").GetString()!;
                result[id] = new ProjectSummary(id, project.GetProperty("title").GetString() ?? "",
                    NullIfEmpty(project.GetProperty("icon_url").GetString()));
            }
        }
        return result;
    }

    public async Task<(string Id, string Title)> GetProjectInfoAsync(string idOrSlug)
    {
        using var json = await GetJsonAsync($"{Api}/project/{idOrSlug}");
        return (json.RootElement.GetProperty("id").GetString()!, json.RootElement.GetProperty("title").GetString()!);
    }

    public async Task<Dictionary<string, ContentVersion>> LookupByHashAsync(IEnumerable<string> sha1Hashes)
    {
        var hashes = sha1Hashes.Distinct().ToList();
        if (hashes.Count == 0)
            return [];

        using var content = new StringContent(JsonSerializer.Serialize(new { hashes, algorithm = "sha1" }),
            Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"{Api}/version_files", content);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => ParseVersion(p.Value));
    }

    private static List<ContentVersion> ParseVersions(JsonElement array) =>
        array.EnumerateArray().Select(ParseVersion).OrderByDescending(v => v.Date).ToList();

    private static ContentVersion ParseVersion(JsonElement v)
    {
        var files = v.GetProperty("files").EnumerateArray().ToList();
        var file = files.FirstOrDefault(f => f.GetProperty("primary").GetBoolean(), files[0]);

        List<string> Dependencies(string kind) => v.GetProperty("dependencies").EnumerateArray()
            .Where(d => d.GetProperty("dependency_type").GetString() == kind
                        && d.TryGetProperty("project_id", out var p) && p.ValueKind == JsonValueKind.String)
            .Select(d => d.GetProperty("project_id").GetString()!)
            .ToList();

        return new ContentVersion
        {
            Id = v.GetProperty("id").GetString()!,
            ProjectId = v.GetProperty("project_id").GetString()!,
            Name = v.GetProperty("version_number").GetString()!,
            FileName = file.GetProperty("filename").GetString()!,
            DownloadUrl = file.GetProperty("url").GetString(),
            Size = file.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
            Date = v.GetProperty("date_published").GetDateTime(),
            Channel = v.GetProperty("version_type").GetString() ?? "release",
            GameVersions = v.GetProperty("game_versions").EnumerateArray().Select(g => g.GetString()!).ToList(),
            Loaders = v.GetProperty("loaders").EnumerateArray().Select(l => l.GetString()!.ToLowerInvariant()).ToList(),
            RequiredProjectIds = Dependencies("required"),
            IncompatibleProjectIds = Dependencies("incompatible")
        };
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
