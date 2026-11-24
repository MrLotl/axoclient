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

        public async Task<SearchPage> Search(SearchQuery query, CancellationToken cancellationToken)
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
                facets.Add(LoaderCategories(loader).Select(category => $"categories:{category}").ToArray());
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

            using var json = JsonDocument.Parse(await _api.GetString(url, _searchCache, cancellationToken));
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
                    _textHelper.Clean(readString(hit, "author"), 60),
                    _textHelper.Clean(readString(hit, "description"), 400),
                    _urlHelper.SafeUrl(readString(hit, "icon_url")),
                    _urlHelper.SafeUrl(hit.TryGetProperty("featured_gallery", out var featuredGallery) && featuredGallery.ValueKind == JsonValueKind.String
                        ? featuredGallery.GetString()
                        : strings(hit, "gallery").FirstOrDefault()),
                    hit.TryGetProperty("downloads", out var downloads) ? downloads.GetInt64() : 0,
                    categories.Where(category => !isLoaderCategory(category)).ToList(),
                    strings(hit, "categories").Where(isLoaderCategory).ToList(),
                    strings(hit, "versions"),
                    hit.TryGetProperty("date_modified", out var dateModified) && dateModified.ValueKind == JsonValueKind.String ? dateModified.GetDateTimeOffset() : null,
                    $"https://modrinth.com/{projectType}/{Uri.EscapeDataString(slug)}");
            }).ToList();
            return new SearchPage(items, json.RootElement.GetProperty("total_hits").GetInt32());
        }

        public async Task<ContentProject> GetProject(string id, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await _api.GetString($"{Api}/project/{Uri.EscapeDataString(id)}", _versionCache, cancellationToken));
            return parseProject(json.RootElement);
        }

        public async Task<IReadOnlyDictionary<string, ContentProject>> GetProjects(IEnumerable<string> ids, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, ContentProject>();
            foreach (var chunk in ids.Distinct().Chunk(IdsPerRequest))
            {
                using var json = JsonDocument.Parse(await _api.GetString(
                    $"{Api}/projects?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}", _versionCache, cancellationToken));
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
        public async Task<IReadOnlyDictionary<string, string>> ServerSides(IEnumerable<string> ids, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, string>();
            foreach (var chunk in ids.Distinct().Chunk(IdsPerRequest))
            {
                using var json = JsonDocument.Parse(await _api.GetString(
                    $"{Api}/projects?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}", _versionCache, cancellationToken));
                foreach (var projectElement in json.RootElement.EnumerateArray())
                {
                    if (projectElement.TryGetProperty("id", out var id) && id.GetString() is { } key)
                    {
                        result[key] = readString(projectElement, "server_side") ?? "unknown";
                    }
                }
            }
            return result;
        }

        private ContentProject parseProject(JsonElement projectElement)
        {
            var type = readString(projectElement, "project_type") ?? "mod";
            var slug = readString(projectElement, "slug") ?? "";
            return new ContentProject(Source, projectElement.GetProperty("id").GetString()!, slug,
                _textHelper.Clean(readString(projectElement, "title"), 100), "", _textHelper.Clean(readString(projectElement, "description"), 400), _urlHelper.SafeUrl(readString(projectElement, "icon_url")),
                null, projectElement.TryGetProperty("downloads", out var downloads) ? downloads.GetInt64() : 0,
                strings(projectElement, "categories").Where(category => !isLoaderCategory(category)).ToList(),
                strings(projectElement, "loaders"), strings(projectElement, "game_versions"),
                projectElement.TryGetProperty("updated", out var updated) && updated.ValueKind == JsonValueKind.String ? updated.GetDateTimeOffset() : null,
                $"https://modrinth.com/{type}/{Uri.EscapeDataString(slug)}");
        }

        public async Task<IReadOnlyList<ContentVersion>> GetVersions(string projectId, string? minecraftVersion,
            LoaderType? loader, ProjectKind kind, CancellationToken cancellationToken)
        {
            var url = $"{Api}/project/{Uri.EscapeDataString(projectId)}/version?include_changelog=false";
            if (minecraftVersion != null)
            {
                url += $"&game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion }))}";
            }

            if (loader is { } loaderType && loaderType != LoaderType.Vanilla && kind is ProjectKind.Mod or ProjectKind.Modpack)
            {
                url += $"&loaders={Uri.EscapeDataString(JsonSerializer.Serialize(LoaderCategories(loaderType)))}";
            }
            else if (kind == ProjectKind.DataPack)
            {
                url += $"&loaders={Uri.EscapeDataString("[\"datapack\"]")}";
            }
            else if (kind == ProjectKind.ShaderPack)
            {
                url += $"&loaders={Uri.EscapeDataString("[\"iris\",\"optifine\",\"canvas\",\"vanilla\"]")}";
            }

            using var json = JsonDocument.Parse(await _api.GetString(url, _versionCache, cancellationToken));
            return json.RootElement.EnumerateArray().Select(ParseVersion).OrderByDescending(version => version.Published).ToList();
        }

        public async Task<ContentVersion?> GetVersion(string versionId, CancellationToken cancellationToken)
        {
            try
            {
                using var json = JsonDocument.Parse(await _api.GetString($"{Api}/version/{Uri.EscapeDataString(versionId)}",
                    _versionCache, cancellationToken));
                return ParseVersion(json.RootElement);
            }
            catch (AxoException exception) when (exception.Code == "errors.notFound")
            {
                return null;
            }
        }

        public async Task<IReadOnlyDictionary<string, ContentVersion>> GetVersionsByIds(IEnumerable<string> ids, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, ContentVersion>();
            foreach (var chunk in ids.Distinct().Chunk(IdsPerRequest))
            {
                using var json = JsonDocument.Parse(await _api.GetString(
                    $"{Api}/versions?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}", _versionCache, cancellationToken));
                foreach (var version in json.RootElement.EnumerateArray().Select(ParseVersion))
                {
                    result[version.Id] = version;
                }
            }
            return result;
        }

        public async Task<IReadOnlyDictionary<string, ContentVersion>> Identify(IReadOnlyCollection<LocalFileHash> files,
            CancellationToken cancellationToken)
        {
            var hashes = files.Select(file => file.Sha1).Distinct().ToList();
            if (hashes.Count == 0)
            {
                return new Dictionary<string, ContentVersion>();
            }

            var result = new Dictionary<string, ContentVersion>();
            foreach (var chunk in hashes.Chunk(500))
            {
                var response = await _api.PostJson<Dictionary<string, JsonElement>>($"{Api}/version_files",
                    new { hashes = chunk, algorithm = "sha1" }, cancellationToken);
                foreach (var (hash, version) in response ?? [])
                {
                    result[hash] = ParseVersion(version);
                }
            }
            return result;
        }

        /// <summary>Neueste passende Version für viele Dateien auf einmal (Update-Prüfung).</summary>
        public async Task<IReadOnlyDictionary<string, ContentVersion>> LatestForHashes(IReadOnlyCollection<string> sha1,
            string minecraftVersion, LoaderType? loader, ProjectKind kind, CancellationToken cancellationToken)
        {
            if (sha1.Count == 0)
            {
                return new Dictionary<string, ContentVersion>();
            }

            var loaders = loader is { } loaderType && loaderType != LoaderType.Vanilla && kind == ProjectKind.Mod
                ? LoaderCategories(loaderType)
                : kind == ProjectKind.ShaderPack ? ["iris", "optifine"] : kind == ProjectKind.ResourcePack ? ["minecraft"] : [];
            var result = new Dictionary<string, ContentVersion>();
            foreach (var chunk in sha1.Distinct().Chunk(500))
            {
                object body = loaders.Length > 0
                    ? new { hashes = chunk, algorithm = "sha1", loaders, game_versions = new[] { minecraftVersion } }
                    : new { hashes = chunk, algorithm = "sha1", game_versions = new[] { minecraftVersion } };
                var response = await _api.PostJson<Dictionary<string, JsonElement>>($"{Api}/version_files/update", body, cancellationToken);
                foreach (var (hash, version) in response ?? [])
                {
                    result[hash] = ParseVersion(version);
                }
            }
            return result;
        }

        internal ContentVersion ParseVersion(JsonElement versionElement)
        {
            var files = versionElement.GetProperty("files").EnumerateArray().Select(file =>
            {
                var hashes = file.GetProperty("hashes");
                return new ContentFile(file.GetProperty("url").GetString()!, file.GetProperty("filename").GetString()!,
                    hashes.TryGetProperty("sha1", out var sha1) ? sha1.GetString() : null,
                    hashes.TryGetProperty("sha512", out var sha512) ? sha512.GetString() : null,
                    file.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
                    file.TryGetProperty("primary", out var primary) && primary.GetBoolean());
            }).ToList();

            var otherDependencies = versionElement.TryGetProperty("dependencies", out var dependencies)
                ? dependencies.EnumerateArray().Select(dependency => new ContentDependency(
                    readString(dependency, "project_id"), readString(dependency, "version_id"),
                    readString(dependency, "dependency_type") switch
                    {
                        "required" => DependencyKind.Required,
                        "incompatible" => DependencyKind.Incompatible,
                        "embedded" => DependencyKind.Embedded,
                        _ => DependencyKind.Optional
                    })).ToList()
                : [];

            return new ContentVersion(Source, versionElement.GetProperty("id").GetString()!, versionElement.GetProperty("project_id").GetString()!,
                _textHelper.Clean(readString(versionElement, "name"), 120), _textHelper.Clean(readString(versionElement, "version_number"), 80), readString(versionElement, "version_type") ?? "release",
                versionElement.GetProperty("date_published").GetDateTimeOffset(), strings(versionElement, "game_versions"),
                strings(versionElement, "loaders").Select(loader => loader.ToLowerInvariant()).ToList(), files, otherDependencies,
                _textHelper.CleanMultiline(readString(versionElement, "changelog"), 20_000) is { Length: > 0 } changelog ? changelog : null);
        }

        public string[] LoaderCategories(LoaderType loader) => loader switch
        {
            // Quilt lädt auch Fabric-Mods
            LoaderType.Quilt => ["quilt", "fabric"],
            _ => [_loaderNames.ModrinthId(loader)]
        };

        private bool isLoaderCategory(string category) =>
            category is "fabric" or "forge" or "neoforge" or "quilt" or "liteloader" or "modloader" or "rift" or "iris" or "optifine"
                or "canvas" or "vanilla" or "minecraft" or "bukkit" or "paper" or "spigot" or "velocity" or "datapack";

        private string? readString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var valueElement) && valueElement.ValueKind == JsonValueKind.String ? valueElement.GetString() : null;

        private List<string> strings(JsonElement element, string name) =>
            element.TryGetProperty(name, out var arrayElement) && arrayElement.ValueKind == JsonValueKind.Array
                ? arrayElement.EnumerateArray().Where(arrayItem => arrayItem.ValueKind == JsonValueKind.String).Select(arrayItem => arrayItem.GetString()!).ToList()
                : [];
    }
}
