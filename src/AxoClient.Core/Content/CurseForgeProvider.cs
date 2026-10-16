using System.Text.Json;
using AxoClient.Core.Common;
using AxoClient.Core.Game;

namespace AxoClient.Core.Content;

/// <summary>
/// CurseForge über den Worker-Proxy (<c>/v1/cf/…</c>), der Key liegt nur als Secret im Worker und nie in der .exe.
/// Bis der Key freigeschaltet ist, gilt die Quelle als "Bald verfügbar".
/// </summary>
public sealed class CurseForgeProvider(ApiClient api, string serviceUrl, bool enabled) : IContentProvider
{
    private const int GameId = 432;
    private static readonly TimeSpan SearchCache = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VersionCache = TimeSpan.FromHours(1);

    private string Base => serviceUrl + "/v1/cf";

    public string Source => "curseforge";
    public bool IsAvailable => enabled;

    private static int ClassId(ProjectKind kind) => kind switch
    {
        ProjectKind.Mod => 6,
        ProjectKind.ResourcePack => 12,
        ProjectKind.ShaderPack => 6552,
        _ => 4471
    };

    private static int? LoaderId(LoaderType? loader) => loader switch
    {
        LoaderType.Forge => 1,
        LoaderType.Fabric => 4,
        LoaderType.Quilt => 5,
        LoaderType.NeoForge => 6,
        _ => null
    };

    private static string LoaderName(int id) => id switch
    {
        1 => "forge",
        4 => "fabric",
        5 => "quilt",
        6 => "neoforge",
        _ => ""
    };

    public async Task<SearchPage> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var sort = query.Sort switch
        {
            SearchSort.Newest => 11,
            SearchSort.Updated => 3,
            SearchSort.Relevance => 1,
            _ => 6
        };
        var url = $"{Base}/mods/search?gameId={GameId}&classId={ClassId(query.Kind)}&pageSize={pageSize}" +
                  $"&index={Math.Max(0, query.Page) * pageSize}&sortField={sort}&sortOrder=desc" +
                  $"&searchFilter={Uri.EscapeDataString(Text.Clean(query.Text, 100))}";
        if (query.MinecraftVersion != null)
            url += $"&gameVersion={Uri.EscapeDataString(query.MinecraftVersion)}";
        if (query.Kind is ProjectKind.Mod or ProjectKind.Modpack && LoaderId(query.Loader) is { } loader)
            url += $"&modLoaderType={loader}";

        using var json = JsonDocument.Parse(await api.GetStringAsync(url, SearchCache, ct));
        var items = json.RootElement.GetProperty("data").EnumerateArray().Select(ParseProject).ToList();
        var total = json.RootElement.TryGetProperty("pagination", out var p) ? Math.Min(p.GetProperty("totalCount").GetInt32(), 10_000) : items.Count;
        return new SearchPage(items, total);
    }

    public async Task<ContentProject> GetProjectAsync(string id, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await api.GetStringAsync($"{Base}/mods/{RequireNumeric(id)}", VersionCache, ct));
        return ParseProject(json.RootElement.GetProperty("data"));
    }

    public async Task<IReadOnlyDictionary<string, ContentProject>> GetProjectsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var numeric = ids.Distinct().Select(RequireNumeric).ToList();
        if (numeric.Count == 0)
            return new Dictionary<string, ContentProject>();
        var response = await api.PostJsonAsync<JsonElement>($"{Base}/mods", new { modIds = numeric }, ct);
        return response.GetProperty("data").EnumerateArray().Select(ParseProject).ToDictionary(p => p.Id);
    }

    public async Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(string projectId, string? minecraftVersion,
        LoaderType? loader, ProjectKind kind, CancellationToken ct)
    {
        var url = $"{Base}/mods/{RequireNumeric(projectId)}/files?pageSize=50";
        if (minecraftVersion != null)
            url += $"&gameVersion={Uri.EscapeDataString(minecraftVersion)}";
        if (kind == ProjectKind.Mod && LoaderId(loader) is { } l)
            url += $"&modLoaderType={l}";
        using var json = JsonDocument.Parse(await api.GetStringAsync(url, VersionCache, ct));
        return json.RootElement.GetProperty("data").EnumerateArray().Select(ParseFile).OrderByDescending(v => v.Published).ToList();
    }

    public async Task<ContentVersion?> GetVersionAsync(string versionId, CancellationToken ct)
    {
        // CurseForge-Versionen werden als "<modId>:<fileId>" gespeichert
        var parts = versionId.Split(':');
        if (parts.Length != 2)
            return null;
        using var json = JsonDocument.Parse(await api.GetStringAsync(
            $"{Base}/mods/{RequireNumeric(parts[0])}/files/{RequireNumeric(parts[1])}", VersionCache, ct));
        return ParseFile(json.RootElement.GetProperty("data"));
    }

    public async Task<IReadOnlyDictionary<string, ContentVersion>> IdentifyAsync(IReadOnlyCollection<LocalFileHash> files,
        CancellationToken ct)
    {
        var withPrint = files.Where(f => f.Fingerprint != null).ToList();
        if (withPrint.Count == 0)
            return new Dictionary<string, ContentVersion>();
        var response = await api.PostJsonAsync<JsonElement>($"{Base}/fingerprints/{GameId}",
            new { fingerprints = withPrint.Select(f => f.Fingerprint!.Value).ToArray() }, ct);
        var byPrint = withPrint.ToDictionary(f => f.Fingerprint!.Value, f => f.Sha1);
        var result = new Dictionary<string, ContentVersion>();
        foreach (var match in response.GetProperty("data").GetProperty("exactMatches").EnumerateArray())
        {
            var file = match.GetProperty("file");
            var print = file.GetProperty("fileFingerprint").GetUInt32();
            if (byPrint.TryGetValue(print, out var sha1))
                result[sha1] = ParseFile(file);
        }
        return result;
    }

    private ContentProject ParseProject(JsonElement m)
    {
        var id = m.GetProperty("id").GetInt32().ToString();
        var slug = m.TryGetProperty("slug", out var s) ? s.GetString() ?? "" : "";
        var links = m.TryGetProperty("links", out var l) ? l : default;
        var logo = m.TryGetProperty("logo", out var lo) && lo.ValueKind == JsonValueKind.Object ? lo.GetProperty("thumbnailUrl").GetString() : null;
        var screenshots = m.TryGetProperty("screenshots", out var sc) && sc.ValueKind == JsonValueKind.Array && sc.GetArrayLength() > 0
            ? sc[0].GetProperty("url").GetString()
            : null;
        var latest = m.TryGetProperty("latestFilesIndexes", out var lfi) ? lfi.EnumerateArray().ToList() : [];
        return new ContentProject(Source, id, slug, Text.Clean(m.GetProperty("name").GetString(), 100),
            m.TryGetProperty("authors", out var a) && a.GetArrayLength() > 0 ? Text.Clean(a[0].GetProperty("name").GetString(), 60) : "",
            Text.Clean(m.TryGetProperty("summary", out var sum) ? sum.GetString() : "", 400),
            ModrinthProvider.SafeUrl(logo), ModrinthProvider.SafeUrl(screenshots),
            m.TryGetProperty("downloadCount", out var dc) ? (long)dc.GetDouble() : 0,
            m.TryGetProperty("categories", out var cats)
                ? cats.EnumerateArray().Select(c => Text.Slug(c.GetProperty("name").GetString() ?? "")).Take(4).ToList()
                : [],
            latest.Where(x => x.TryGetProperty("modLoader", out var ml) && ml.ValueKind == JsonValueKind.Number)
                .Select(x => LoaderName(x.GetProperty("modLoader").GetInt32())).Where(x => x.Length > 0).Distinct().ToList(),
            latest.Select(x => x.GetProperty("gameVersion").GetString()!).Distinct().ToList(),
            m.TryGetProperty("dateModified", out var dm) ? dm.GetDateTimeOffset() : null,
            links.ValueKind == JsonValueKind.Object && links.TryGetProperty("websiteUrl", out var w) && w.GetString() is { } web
                ? web
                : $"https://www.curseforge.com/minecraft/mc-mods/{Uri.EscapeDataString(slug)}");
    }

    private ContentVersion ParseFile(JsonElement f)
    {
        var modId = f.GetProperty("modId").GetInt32().ToString();
        var fileId = f.GetProperty("id").GetInt32().ToString();
        var hashes = f.TryGetProperty("hashes", out var h) ? h.EnumerateArray().ToList() : [];
        var sha1 = hashes.FirstOrDefault(x => x.GetProperty("algo").GetInt32() == 1) is { ValueKind: JsonValueKind.Object } sh
            ? sh.GetProperty("value").GetString()
            : null;
        var gameVersions = f.TryGetProperty("gameVersions", out var gv) ? gv.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        var loaders = gameVersions.Where(x => x is "Forge" or "Fabric" or "Quilt" or "NeoForge" or "Iris" or "OptiFine")
            .Select(x => x.ToLowerInvariant()).ToList();
        // Mods ohne Freigabe für Drittanbieter haben keine Download-URL: der Launcher zeigt dann einen Dialog
        var url = f.TryGetProperty("downloadUrl", out var du) && du.ValueKind == JsonValueKind.String ? du.GetString()! : "";
        var deps = f.TryGetProperty("dependencies", out var d)
            ? d.EnumerateArray().Select(x => new ContentDependency(x.GetProperty("modId").GetInt32().ToString(), null,
                x.GetProperty("relationType").GetInt32() switch
                {
                    3 => DependencyKind.Required,
                    5 => DependencyKind.Incompatible,
                    1 => DependencyKind.Embedded,
                    _ => DependencyKind.Optional
                })).ToList()
            : [];
        return new ContentVersion(Source, $"{modId}:{fileId}", modId, Text.Clean(f.GetProperty("displayName").GetString(), 120),
            Text.Clean(f.GetProperty("displayName").GetString(), 80),
            f.TryGetProperty("releaseType", out var rt) ? rt.GetInt32() switch { 2 => "beta", 3 => "alpha", _ => "release" } : "release",
            f.GetProperty("fileDate").GetDateTimeOffset(),
            gameVersions.Where(x => char.IsDigit(x[0])).ToList(), loaders,
            [new ContentFile(url, f.GetProperty("fileName").GetString()!, sha1, null, f.GetProperty("fileLength").GetInt64(), true)],
            deps);
    }

    private static string RequireNumeric(string id) =>
        id.Length is > 0 and < 12 && id.All(char.IsAsciiDigit) ? id : throw new AxoException("errors.invalidId");

    /// <summary>CurseForge-Fingerprint: Murmur2 (Seed 1) über die Datei ohne Leerraum-Bytes.</summary>
    public static uint Fingerprint(ReadOnlySpan<byte> data)
    {
        var filtered = new List<byte>(data.Length);
        foreach (var b in data)
            if (b is not (9 or 10 or 13 or 32))
                filtered.Add(b);
        return Murmur2(filtered.ToArray(), 1);
    }

    private static uint Murmur2(byte[] data, uint seed)
    {
        const uint m = 0x5bd1e995;
        const int r = 24;
        var length = data.Length;
        var h = seed ^ (uint)length;
        var i = 0;
        while (length >= 4)
        {
            var k = (uint)(data[i] | data[i + 1] << 8 | data[i + 2] << 16 | data[i + 3] << 24);
            k *= m;
            k ^= k >> r;
            k *= m;
            h *= m;
            h ^= k;
            i += 4;
            length -= 4;
        }
        switch (length)
        {
            case 3:
                h ^= (uint)data[i + 2] << 16;
                goto case 2;
            case 2:
                h ^= (uint)data[i + 1] << 8;
                goto case 1;
            case 1:
                h ^= data[i];
                h *= m;
                break;
        }
        h ^= h >> 13;
        h *= m;
        h ^= h >> 15;
        return h;
    }
}
