using System.Net.Http;
using System.Text.Json;

namespace McLauncher;

/// <summary>Suche und Downloads über die Modrinth-API (kein Schlüssel nötig).</summary>
public class ModrinthProvider(HttpClient http) : IContentProvider
{
    private const string Api = "https://api.modrinth.com/v2";

    public ContentSource Source => ContentSource.Modrinth;

    public async Task<SearchPage> SearchAsync(string query, ContentType type, Installation inst, int page, int pageSize)
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
            facets.Add([$"categories:{LoaderName(inst)}"]);

        var url = $"{Api}/search?limit={pageSize}&offset={page * pageSize}" +
                  $"&index={(string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance")}" +
                  $"&query={Uri.EscapeDataString(query)}" +
                  $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";

        using var json = await GetJsonAsync(url);
        var items = json.RootElement.GetProperty("hits").EnumerateArray().Select(hit => new ContentProject
        {
            Id = hit.GetProperty("project_id").GetString()!,
            Title = hit.GetProperty("title").GetString()!,
            Source = ContentSource.Modrinth,
            Author = hit.GetProperty("author").GetString() ?? "",
            Description = hit.GetProperty("description").GetString() ?? "",
            IconUrl = NullIfEmpty(hit.GetProperty("icon_url").GetString()),
            Downloads = hit.GetProperty("downloads").GetInt64(),
            WebsiteUrl = $"https://modrinth.com/{projectType}/{hit.GetProperty("slug").GetString()}"
        }).ToList();
        return new SearchPage(items, json.RootElement.GetProperty("total_hits").GetInt32());
    }

    public async Task<List<ContentVersion>> GetVersionsAsync(string projectId, ContentType type, Installation inst)
    {
        var url = $"{Api}/project/{projectId}/version" +
                  $"?game_versions={Uri.EscapeDataString($"[\"{inst.MinecraftVersion}\"]")}";
        if (type == ContentType.Mod)
            url += $"&loaders={Uri.EscapeDataString($"[\"{LoaderName(inst)}\"]")}";

        using var json = await GetJsonAsync(url);
        return json.RootElement.EnumerateArray().Select(ParseVersion).OrderByDescending(v => v.Date).ToList();
    }

    public async Task<ContentVersion?> GetVersionAsync(string projectId, string versionId)
    {
        try
        {
            using var json = await GetJsonAsync($"{Api}/version/{versionId}");
            return ParseVersion(json.RootElement);
        }
        catch (HttpRequestException)
        {
            return null; // Version wurde vom Autor gelöscht
        }
    }

    /// <summary>
    /// Findet Versionen anhand des SHA-1 der Dateien. So lassen sich auch manuell hinzugefügte Mods zuordnen.
    /// Ergebnis: SHA-1 → Version (nur für Dateien, die Modrinth kennt).
    /// </summary>
    public async Task<Dictionary<string, ContentVersion>> LookupByHashAsync(IEnumerable<string> sha1Hashes)
    {
        var hashes = sha1Hashes.Distinct().ToList();
        if (hashes.Count == 0)
            return [];

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Api}/version_files")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { hashes, algorithm = "sha1" }),
                System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.UserAgent.ParseAdd("AxoClient/1.0");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => ParseVersion(p.Value));
    }

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
            Source = ContentSource.Modrinth,
            Name = v.GetProperty("version_number").GetString()!,
            FileName = file.GetProperty("filename").GetString()!,
            DownloadUrl = file.GetProperty("url").GetString(),
            Date = v.GetProperty("date_published").GetDateTime(),
            Channel = v.GetProperty("version_type").GetString() ?? "release",
            GameVersions = v.GetProperty("game_versions").EnumerateArray().Select(g => g.GetString()!).ToList(),
            Loaders = v.GetProperty("loaders").EnumerateArray().Select(l => l.GetString()!.ToLowerInvariant()).ToList(),
            RequiredProjectIds = Dependencies("required"),
            IncompatibleProjectIds = Dependencies("incompatible")
        };
    }

    public async Task<(string Id, string Title)> GetProjectInfoAsync(string idOrSlug)
    {
        using var json = await GetJsonAsync($"{Api}/project/{idOrSlug}");
        return (json.RootElement.GetProperty("id").GetString()!, json.RootElement.GetProperty("title").GetString()!);
    }

    private static string LoaderName(Installation inst) => inst.Loader == LoaderType.Forge ? "forge" : "fabric";

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Modrinth verlangt einen aussagekräftigen User-Agent
        request.Headers.UserAgent.ParseAdd("AxoClient/1.0");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
