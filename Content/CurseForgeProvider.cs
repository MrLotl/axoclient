using System.Net.Http;
using System.Text.Json;

namespace McLauncher;

/// <summary>Suche und Downloads über die offizielle CurseForge-API (benötigt einen API-Schlüssel).</summary>
public class CurseForgeProvider(HttpClient http, string apiKey) : IContentProvider
{
    private const string Api = "https://api.curseforge.com/v1";
    private const int MinecraftGameId = 432;

    public ContentSource Source => ContentSource.CurseForge;

    // CurseForge liefert höchstens die ersten 10.000 Treffer einer Suche (index + pageSize <= 10.000)
    private const int MaxResults = 10_000;

    public async Task<SearchPage> SearchAsync(string query, ContentType type, Installation inst, int page, int pageSize)
    {
        var url = $"{Api}/mods/search?gameId={MinecraftGameId}&classId={ClassId(type)}" +
                  $"&gameVersion={Uri.EscapeDataString(inst.MinecraftVersion)}" +
                  $"&searchFilter={Uri.EscapeDataString(query)}" +
                  $"&sortField=2&sortOrder=desc&pageSize={pageSize}&index={page * pageSize}"; // 2 = Beliebtheit
        if (type == ContentType.Mod)
            url += $"&modLoaderType={LoaderId(inst)}";

        using var json = await GetJsonAsync(url);
        var total = json.RootElement.GetProperty("pagination").GetProperty("totalCount").GetInt32();
        var items = json.RootElement.GetProperty("data").EnumerateArray().Select(mod => new ContentProject
        {
            Id = mod.GetProperty("id").GetInt32().ToString(),
            Title = mod.GetProperty("name").GetString()!,
            Source = ContentSource.CurseForge,
            Author = mod.GetProperty("authors").EnumerateArray().Select(a => a.GetProperty("name").GetString())
                .FirstOrDefault() ?? "",
            Description = mod.GetProperty("summary").GetString() ?? "",
            IconUrl = mod.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object
                ? logo.GetProperty("thumbnailUrl").GetString()
                : null,
            Downloads = (long)mod.GetProperty("downloadCount").GetDouble(),
            WebsiteUrl = mod.GetProperty("links").GetProperty("websiteUrl").GetString()
        }).ToList();
        return new SearchPage(items, Math.Min(total, MaxResults));
    }

    public async Task<List<ContentVersion>> GetVersionsAsync(string projectId, ContentType type, Installation inst)
    {
        var url = $"{Api}/mods/{projectId}/files?gameVersion={Uri.EscapeDataString(inst.MinecraftVersion)}&pageSize=50";
        if (type == ContentType.Mod)
            url += $"&modLoaderType={LoaderId(inst)}";

        using var json = await GetJsonAsync(url);
        return json.RootElement.GetProperty("data").EnumerateArray()
            .Select(ParseFile)
            .OrderByDescending(v => v.Date)
            .ToList();
    }

    public async Task<ContentVersion?> GetVersionAsync(string projectId, string versionId)
    {
        try
        {
            using var json = await GetJsonAsync($"{Api}/mods/{projectId}/files/{versionId}");
            return ParseFile(json.RootElement.GetProperty("data"));
        }
        catch (HttpRequestException)
        {
            return null; // Datei wurde vom Autor gelöscht
        }
    }

    private static ContentVersion ParseFile(JsonElement f)
    {
        // CurseForge mischt Minecraft-Versionen und Loader-Namen ("Forge", "Fabric", "Client") in gameVersions
        var tags = f.GetProperty("gameVersions").EnumerateArray().Select(g => g.GetString()!).ToList();
        string[] loaderNames = ["forge", "fabric", "neoforge", "quilt"];

        List<string> Dependencies(int relation) => f.GetProperty("dependencies").EnumerateArray()
            .Where(d => d.GetProperty("relationType").GetInt32() == relation)
            .Select(d => d.GetProperty("modId").GetInt32().ToString())
            .ToList();

        return new ContentVersion
        {
            Id = f.GetProperty("id").GetInt32().ToString(),
            ProjectId = f.GetProperty("modId").GetInt32().ToString(),
            Source = ContentSource.CurseForge,
            Name = f.GetProperty("displayName").GetString()!,
            FileName = f.GetProperty("fileName").GetString()!,
            // null: Autor hat Downloads über Drittanbieter-Launcher deaktiviert
            DownloadUrl = f.GetProperty("downloadUrl").ValueKind == JsonValueKind.String
                ? f.GetProperty("downloadUrl").GetString()
                : null,
            Date = f.GetProperty("fileDate").GetDateTime(),
            // releaseType: 1 = Release, 2 = Beta, 3 = Alpha
            Channel = f.GetProperty("releaseType").GetInt32() switch { 2 => "beta", 3 => "alpha", _ => "release" },
            GameVersions = tags.Where(t => char.IsDigit(t[0])).ToList(),
            Loaders = tags.Select(t => t.ToLowerInvariant()).Where(loaderNames.Contains).ToList(),
            RequiredProjectIds = Dependencies(3),     // 3 = benötigt
            IncompatibleProjectIds = Dependencies(5)  // 5 = inkompatibel
        };
    }

    public async Task<(string Id, string Title)> GetProjectInfoAsync(string idOrSlug)
    {
        using var json = await GetJsonAsync($"{Api}/mods/{idOrSlug}");
        var data = json.RootElement.GetProperty("data");
        return (data.GetProperty("id").GetInt32().ToString(), data.GetProperty("name").GetString()!);
    }

    private static int ClassId(ContentType type) => type switch
    {
        ContentType.Mod => 6,
        ContentType.ResourcePack => 12,
        _ => 6552
    };

    private static int LoaderId(Installation inst) => inst.Loader == LoaderType.Forge ? 1 : 4;

    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        using var response = await http.SendAsync(request);
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("CurseForge hat den API-Schlüssel abgelehnt.");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
