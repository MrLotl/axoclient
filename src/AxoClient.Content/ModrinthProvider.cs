using System.Text.Json;
using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;

namespace AxoClient.Content
{
    /// <summary>Modrinth API v2 (ohne Key, 300 Anfragen pro Minute; Wiederholung bei 429 im <see cref="ApiClient"/>).</summary>
    public sealed class ModrinthProvider : IContentProvider, IModrinthProvider
    {
        private readonly IApiClient _api;
        private readonly ITextHelper _textHelper;
        private readonly ILoaderNames _loaderNames;
        private readonly IUrlHelper _urlHelper;
        private const string Api = "https://api.modrinth.com/v2";
        private const int IdsPerRequest = 50;

        private readonly TimeSpan _searchCache = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _versionCache = TimeSpan.FromHours(1);

        public ModrinthProvider(IApiClient api, ITextHelper textHelper, ILoaderNames loaderNames, IUrlHelper urlHelper)
        {
            _api = api;
            _textHelper = textHelper;
            _loaderNames = loaderNames;
            _urlHelper = urlHelper;
        }

        public string Source => "modrinth";
        public bool IsAvailable => true;

        public string ProjectType(ProjectKind kind) => kind switch
        {
            ProjectKind.Mod => "mod",
            ProjectKind.ResourcePack => "resourcepack",
            ProjectKind.ShaderPack => "shader",
            ProjectKind.DataPack => "datapack",
            _ => "modpack"
        };

        public async Task<SearchPage> Search(SearchQuery query, CancellationToken ct)
        {
            var projectType = ProjectType(query.Kind);
            // Datapacks sind bei Modrinth meist Projekte vom Typ "mod" mit dem Loader "datapack"
            var facets = new List<string[]> { query.Kind == ProjectKind.DataPack ? ["categories:datapack"] : new[] { $"project_type:{projectType}" } };
            if (query.MinecraftVersion != null)
            {
                facets.Add([$"versions:{query.MinecraftVersion}"]);
            }

            if (query.Loader is { } loader && loader != LoaderType.Vanilla && query.Kind is ProjectKind.Mod or ProjectKind.Modpack)
            {
                facets.Add(LoaderCategories(loader).Select(c => $"categories:{c}").ToArray());
            }
            else if (query.Kind == ProjectKind.Modpack)
            {
                facets.Add(["categories:fabric", "categories:forge", "categories:neoforge", "categories:quilt"]);
            }

            if (!string.IsNullOrWhiteSpace(query.Category) && _textHelper.IsValidId(query.Category))
            {
                facets.Add([$"categories:{query.Category}"]);
            }

            var index = query.Sort switch
            {
                SearchSort.Newest => "newest",
                SearchSort.Updated => "updated",
                SearchSort.Relevance when !string.IsNullOrWhiteSpace(query.Text) => "relevance",
                _ => "downloads"
            };
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var url = $"{Api}/search?limit={pageSize}&offset={Math.Max(0, query.Page) * pageSize}&index={index}" +
                      $"&query={Uri.EscapeDataString(_textHelper.Clean(query.Text, 100))}" +
                      $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";

            using var json = JsonDocument.Parse(await _api.GetString(url, _searchCache, ct));
            var items = json.RootElement.GetProperty("hits").EnumerateArray().Select(hit =>
            {
                var categories = strings(hit, "display_categories");
                if (categories.Count == 0)
                {
                    categories = strings(hit, "categories");
                }

                var slug = hit.GetProperty("slug").GetString() ?? "";
                return new ContentProject(Source,
                    hit.GetProperty("project_id").GetString()!,
                    slug,
                    _textHelper.Clean(hit.GetProperty("title").GetString(), 100),
                    _textHelper.Clean(str(hit, "author"), 60),
                    _textHelper.Clean(str(hit, "description"), 400),
                    _urlHelper.SafeUrl(str(hit, "icon_url")),
                    _urlHelper.SafeUrl(hit.TryGetProperty("featured_gallery", out var g) && g.ValueKind == JsonValueKind.String
                        ? g.GetString()
                        : strings(hit, "gallery").FirstOrDefault()),
                    hit.TryGetProperty("downloads", out var d) ? d.GetInt64() : 0,
                    categories.Where(c => !isLoaderCategory(c)).ToList(),
                    strings(hit, "categories").Where(isLoaderCategory).ToList(),
                    strings(hit, "versions"),
                    hit.TryGetProperty("date_modified", out var m) && m.ValueKind == JsonValueKind.String ? m.GetDateTimeOffset() : null,
                    $"https://modrinth.com/{projectType}/{Uri.EscapeDataString(slug)}");
            }).ToList();
            return new SearchPage(items, json.RootElement.GetProperty("total_hits").GetInt32());
        }

        public async Task<ContentProject> GetProject(string id, CancellationToken ct)
        {
            using var json = JsonDocument.Parse(await _api.GetString($"{Api}/project/{Uri.EscapeDataString(id)}", _versionCache, ct));
            return parseProject(json.RootElement);
        }

        public async Task<IReadOnlyDictionary<string, ContentProject>> GetProjects(IEnumerable<string> ids, CancellationToken ct)
        {
            var result = new Dictionary<string, ContentProject>();
            foreach (var chunk in ids.Distinct().Chunk(IdsPerRequest))
            {
                using var json = JsonDocument.Parse(await _api.GetString(
                    $"{Api}/projects?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}", _versionCache, ct));
                foreach (var project in json.RootElement.EnumerateArray().Select(parseProject))
                {
                    result[project.Id] = project;
                }
            }
            return result;
        }

        /// <summary>
        /// <c>server_side</c> je Projekt (<c>required</c>, <c>optional</c>, <c>unsupported</c>, <c>unknown</c>): beim Nachjoinen in eine
        /// offene Welt zählen reine Client-Mods des Gastgebers nicht als „fehlt“.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, string>> ServerSides(IEnumerable<string> ids, CancellationToken ct)
        {
            var result = new Dictionary<string, string>();
            foreach (var chunk in ids.Distinct().Chunk(IdsPerRequest))
            {
                using var json = JsonDocument.Parse(await _api.GetString(
                    $"{Api}/projects?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}", _versionCache, ct));
                foreach (var p in json.RootElement.EnumerateArray())
                {
                    if (p.TryGetProperty("id", out var id) && id.GetString() is { } key)
                    {
                        result[key] = str(p, "server_side") ?? "unknown";
                    }
                }
            }
            return result;
        }

        private ContentProject parseProject(JsonElement p)
        {
            var type = str(p, "project_type") ?? "mod";
            var slug = str(p, "slug") ?? "";
            return new ContentProject(Source, p.GetProperty("id").GetString()!, slug,
                _textHelper.Clean(str(p, "title"), 100), "", _textHelper.Clean(str(p, "description"), 400), _urlHelper.SafeUrl(str(p, "icon_url")),
                null, p.TryGetProperty("downloads", out var d) ? d.GetInt64() : 0,
                strings(p, "categories").Where(c => !isLoaderCategory(c)).ToList(),
                strings(p, "loaders"), strings(p, "game_versions"),
                p.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.String ? u.GetDateTimeOffset() : null,
                $"https://modrinth.com/{type}/{Uri.EscapeDataString(slug)}");
        }

        public async Task<IReadOnlyList<ContentVersion>> GetVersions(string projectId, string? minecraftVersion,
            LoaderType? loader, ProjectKind kind, CancellationToken ct)
        {
            var url = $"{Api}/project/{Uri.EscapeDataString(projectId)}/version?include_changelog=false";
            if (minecraftVersion != null)
            {
                url += $"&game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion }))}";
            }

            if (loader is { } l && l != LoaderType.Vanilla && kind is ProjectKind.Mod or ProjectKind.Modpack)
            {
                url += $"&loaders={Uri.EscapeDataString(JsonSerializer.Serialize(LoaderCategories(l)))}";
            }
            else if (kind == ProjectKind.DataPack)
            {
                url += $"&loaders={Uri.EscapeDataString("[\"datapack\"]")}";
            }
            else if (kind == ProjectKind.ShaderPack)
            {
                url += $"&loaders={Uri.EscapeDataString("[\"iris\",\"optifine\",\"canvas\",\"vanilla\"]")}";
            }

            using var json = JsonDocument.Parse(await _api.GetString(url, _versionCache, ct));
            return json.RootElement.EnumerateArray().Select(ParseVersion).OrderByDescending(v => v.Published).ToList();
        }

        public async Task<ContentVersion?> GetVersion(string versionId, CancellationToken ct)
        {
            try
            {
                using var json = JsonDocument.Parse(await _api.GetString($"{Api}/version/{Uri.EscapeDataString(versionId)}",
                    _versionCache, ct));
                return ParseVersion(json.RootElement);
            }
            catch (AxoException ex) when (ex.Code == "errors.notFound")
            {
                return null;
            }
        }

        public async Task<IReadOnlyDictionary<string, ContentVersion>> GetVersionsByIds(IEnumerable<string> ids, CancellationToken ct)
        {
            var result = new Dictionary<string, ContentVersion>();
            foreach (var chunk in ids.Distinct().Chunk(IdsPerRequest))
            {
                using var json = JsonDocument.Parse(await _api.GetString(
                    $"{Api}/versions?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}", _versionCache, ct));
                foreach (var version in json.RootElement.EnumerateArray().Select(ParseVersion))
                {
                    result[version.Id] = version;
                }
            }
            return result;
        }

        public async Task<IReadOnlyDictionary<string, ContentVersion>> Identify(IReadOnlyCollection<LocalFileHash> files,
            CancellationToken ct)
        {
            var hashes = files.Select(f => f.Sha1).Distinct().ToList();
            if (hashes.Count == 0)
            {
                return new Dictionary<string, ContentVersion>();
            }

            var result = new Dictionary<string, ContentVersion>();
            foreach (var chunk in hashes.Chunk(500))
            {
                var response = await _api.PostJson<Dictionary<string, JsonElement>>($"{Api}/version_files",
                    new { hashes = chunk, algorithm = "sha1" }, ct);
                foreach (var (hash, version) in response ?? [])
                {
                    result[hash] = ParseVersion(version);
                }
            }
            return result;
        }

        /// <summary>Neueste passende Version für viele Dateien auf einmal (Update-Prüfung).</summary>
        public async Task<IReadOnlyDictionary<string, ContentVersion>> LatestForHashes(IReadOnlyCollection<string> sha1,
            string minecraftVersion, LoaderType? loader, ProjectKind kind, CancellationToken ct)
        {
            if (sha1.Count == 0)
            {
                return new Dictionary<string, ContentVersion>();
            }

            var loaders = loader is { } l && l != LoaderType.Vanilla && kind == ProjectKind.Mod
                ? LoaderCategories(l)
                : kind == ProjectKind.ShaderPack ? ["iris", "optifine"] : kind == ProjectKind.ResourcePack ? ["minecraft"] : [];
            var result = new Dictionary<string, ContentVersion>();
            foreach (var chunk in sha1.Distinct().Chunk(500))
            {
                object body = loaders.Length > 0
                    ? new { hashes = chunk, algorithm = "sha1", loaders, game_versions = new[] { minecraftVersion } }
                    : new { hashes = chunk, algorithm = "sha1", game_versions = new[] { minecraftVersion } };
                var response = await _api.PostJson<Dictionary<string, JsonElement>>($"{Api}/version_files/update", body, ct);
                foreach (var (hash, version) in response ?? [])
                {
                    result[hash] = ParseVersion(version);
                }
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
                    str(dep, "project_id"), str(dep, "version_id"),
                    str(dep, "dependency_type") switch
                    {
                        "required" => DependencyKind.Required,
                        "incompatible" => DependencyKind.Incompatible,
                        "embedded" => DependencyKind.Embedded,
                        _ => DependencyKind.Optional
                    })).ToList()
                : [];

            return new ContentVersion(Source, v.GetProperty("id").GetString()!, v.GetProperty("project_id").GetString()!,
                _textHelper.Clean(str(v, "name"), 120), _textHelper.Clean(str(v, "version_number"), 80), str(v, "version_type") ?? "release",
                v.GetProperty("date_published").GetDateTimeOffset(), strings(v, "game_versions"),
                strings(v, "loaders").Select(x => x.ToLowerInvariant()).ToList(), files, deps,
                _textHelper.CleanMultiline(str(v, "changelog"), 20_000) is { Length: > 0 } c ? c : null);
        }

        public string[] LoaderCategories(LoaderType loader) => loader switch
        {
            // Quilt lädt auch Fabric-Mods
            LoaderType.Quilt => ["quilt", "fabric"],
            _ => [_loaderNames.ModrinthId(loader)]
        };

        private bool isLoaderCategory(string c) =>
            c is "fabric" or "forge" or "neoforge" or "quilt" or "liteloader" or "modloader" or "rift" or "iris" or "optifine"
                or "canvas" or "vanilla" or "minecraft" or "bukkit" or "paper" or "spigot" or "velocity" or "datapack";

        private string? str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private List<string> strings(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : [];
    }
}
