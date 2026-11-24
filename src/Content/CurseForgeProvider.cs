using System.Text.Json;
using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;

namespace AxoClient.Content
{
    /// <summary>
    /// CurseForge über den Worker-Proxy (<c>/v1/cf/…</c>), der Key liegt nur als Secret im Worker und nie in der .exe.
    /// Bis der Key freigeschaltet ist, gilt die Quelle als "Bald verfügbar".
    /// </summary>
    public sealed class CurseForgeProvider : IContentProvider
    {
        private readonly IApiClient _api;
        private readonly string _serviceUrl;
        private readonly bool _enabled;
        private readonly ITextHelper _textHelper;
        private readonly IUrlHelper _urlHelper;
        private const int GameId = 432;
        private readonly TimeSpan _searchCache = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _versionCache = TimeSpan.FromHours(1);

        public CurseForgeProvider(IApiClient api, string serviceUrl, bool enabled, ITextHelper textHelper, IUrlHelper urlHelper)
        {
            _api = api;
            _serviceUrl = serviceUrl;
            _enabled = enabled;
            _textHelper = textHelper;
            _urlHelper = urlHelper;
        }

        private string Base => _serviceUrl + "/v1/cf";

        public string Source => "curseforge";
        public bool IsAvailable => _enabled;

        private int classId(ProjectKind kind) => kind switch
        {
            ProjectKind.Mod => 6,
            ProjectKind.ResourcePack => 12,
            ProjectKind.ShaderPack => 6552,
            ProjectKind.DataPack => 6945,
            _ => 4471
        };

        private int? loaderId(LoaderType? loader) => loader switch
        {
            LoaderType.Forge => 1,
            LoaderType.Fabric => 4,
            LoaderType.Quilt => 5,
            LoaderType.NeoForge => 6,
            _ => null
        };

        private string loaderName(int id) => id switch
        {
            1 => "forge",
            4 => "fabric",
            5 => "quilt",
            6 => "neoforge",
            _ => ""
        };

        public async Task<SearchPage> Search(SearchQuery query, CancellationToken cancellationToken)
        {
            var pageSize = Math.Clamp(query.PageSize, 1, 50);
            var sort = query.Sort switch
            {
                SearchSort.Newest => 11,
                SearchSort.Updated => 3,
                SearchSort.Relevance => 1,
                _ => 6
            };
            var url = $"{Base}/mods/search?gameId={GameId}&classId={classId(query.Kind)}&pageSize={pageSize}" +
                      $"&index={Math.Max(0, query.Page) * pageSize}&sortField={sort}&sortOrder=desc" +
                      $"&searchFilter={Uri.EscapeDataString(_textHelper.Clean(query.Text, 100))}";
            if (query.MinecraftVersion != null)
            {
                url += $"&gameVersion={Uri.EscapeDataString(query.MinecraftVersion)}";
            }

            if (query.Kind is ProjectKind.Mod or ProjectKind.Modpack && loaderId(query.Loader) is { } loader)
            {
                url += $"&modLoaderType={loader}";
            }

            using var json = JsonDocument.Parse(await _api.GetString(url, _searchCache, cancellationToken));
            var items = json.RootElement.GetProperty("data").EnumerateArray().Select(parseProject).ToList();
            var total = json.RootElement.TryGetProperty("pagination", out var pagination) ? Math.Min(pagination.GetProperty("totalCount").GetInt32(), 10_000) : items.Count;
            return new SearchPage(items, total);
        }

        public async Task<ContentProject> GetProject(string id, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await _api.GetString($"{Base}/mods/{requireNumeric(id)}", _versionCache, cancellationToken));
            return parseProject(json.RootElement.GetProperty("data"));
        }

        public async Task<IReadOnlyDictionary<string, ContentProject>> GetProjects(IEnumerable<string> ids, CancellationToken cancellationToken)
        {
            var numeric = ids.Distinct().Select(requireNumeric).ToList();
            if (numeric.Count == 0)
            {
                return new Dictionary<string, ContentProject>();
            }

            var response = await _api.PostJson<JsonElement>($"{Base}/mods", new { modIds = numeric }, cancellationToken);
            return response.GetProperty("data").EnumerateArray().Select(parseProject).ToDictionary(contentProject => contentProject.Id);
        }

        public async Task<IReadOnlyList<ContentVersion>> GetVersions(string projectId, string? minecraftVersion,
            LoaderType? loader, ProjectKind kind, CancellationToken cancellationToken)
        {
            var url = $"{Base}/mods/{requireNumeric(projectId)}/files?pageSize=50";
            if (minecraftVersion != null)
            {
                url += $"&gameVersion={Uri.EscapeDataString(minecraftVersion)}";
            }

            if (kind == ProjectKind.Mod && loaderId(loader) is { } loaderTypeId)
            {
                url += $"&modLoaderType={loaderTypeId}";
            }

            using var json = JsonDocument.Parse(await _api.GetString(url, _versionCache, cancellationToken));
            return json.RootElement.GetProperty("data").EnumerateArray().Select(parseFile).OrderByDescending(version => version.Published).ToList();
        }

        public async Task<ContentVersion?> GetVersion(string versionId, CancellationToken cancellationToken)
        {
            // CurseForge-Versionen werden als "<modId>:<fileId>" gespeichert
            var parts = versionId.Split(':');
            if (parts.Length != 2)
            {
                return null;
            }

            using var json = JsonDocument.Parse(await _api.GetString(
                $"{Base}/mods/{requireNumeric(parts[0])}/files/{requireNumeric(parts[1])}", _versionCache, cancellationToken));
            return parseFile(json.RootElement.GetProperty("data"));
        }

        public async Task<IReadOnlyDictionary<string, ContentVersion>> Identify(IReadOnlyCollection<LocalFileHash> files,
            CancellationToken cancellationToken)
        {
            var withPrint = files.Where(file => file.Fingerprint != null).ToList();
            if (withPrint.Count == 0)
            {
                return new Dictionary<string, ContentVersion>();
            }

            var response = await _api.PostJson<JsonElement>($"{Base}/fingerprints/{GameId}",
                new { fingerprints = withPrint.Select(hash => hash.Fingerprint!.Value).ToArray() }, cancellationToken);
            var byPrint = withPrint.ToDictionary(hash => hash.Fingerprint!.Value, hash => hash.Sha1);
            var result = new Dictionary<string, ContentVersion>();
            foreach (var match in response.GetProperty("data").GetProperty("exactMatches").EnumerateArray())
            {
                var file = match.GetProperty("file");
                var print = file.GetProperty("fileFingerprint").GetUInt32();
                if (byPrint.TryGetValue(print, out var sha1))
                {
                    result[sha1] = parseFile(file);
                }
            }
            return result;
        }

        private ContentProject parseProject(JsonElement modElement)
        {
            var id = modElement.GetProperty("id").GetInt32().ToString();
            var slug = modElement.TryGetProperty("slug", out var slugElement) ? slugElement.GetString() ?? "" : "";
            var links = modElement.TryGetProperty("links", out var linksElement) ? linksElement : default;
            var logo = modElement.TryGetProperty("logo", out var logoElement) && logoElement.ValueKind == JsonValueKind.Object ? logoElement.GetProperty("thumbnailUrl").GetString() : null;
            var screenshots = modElement.TryGetProperty("screenshots", out var screenshotsElement) && screenshotsElement.ValueKind == JsonValueKind.Array && screenshotsElement.GetArrayLength() > 0
                ? screenshotsElement[0].GetProperty("url").GetString()
                : null;
            var latest = modElement.TryGetProperty("latestFilesIndexes", out var lfi) ? lfi.EnumerateArray().ToList() : [];
            return new ContentProject(Source, id, slug, _textHelper.Clean(modElement.GetProperty("name").GetString(), 100),
                modElement.TryGetProperty("authors", out var authors) && authors.GetArrayLength() > 0 ? _textHelper.Clean(authors[0].GetProperty("name").GetString(), 60) : "",
                _textHelper.Clean(modElement.TryGetProperty("summary", out var sum) ? sum.GetString() : "", 400),
                _urlHelper.SafeUrl(logo), _urlHelper.SafeUrl(screenshots),
                modElement.TryGetProperty("downloadCount", out var downloadCount) ? (long)downloadCount.GetDouble() : 0,
                modElement.TryGetProperty("categories", out var cats)
                    ? cats.EnumerateArray().Select(category => _textHelper.Slug(category.GetProperty("name").GetString() ?? "")).Take(4).ToList()
                    : [],
                latest.Where(latestFile => latestFile.TryGetProperty("modLoader", out var modLoader) && modLoader.ValueKind == JsonValueKind.Number)
                    .Select(latestFile => loaderName(latestFile.GetProperty("modLoader").GetInt32())).Where(modLoaderName => modLoaderName.Length > 0).Distinct().ToList(),
                latest.Select(latestFile => latestFile.GetProperty("gameVersion").GetString()!).Distinct().ToList(),
                modElement.TryGetProperty("dateModified", out var dateModified) ? dateModified.GetDateTimeOffset() : null,
                links.ValueKind == JsonValueKind.Object && links.TryGetProperty("websiteUrl", out var websiteUrl) && websiteUrl.GetString() is { } web
                    ? web
                    : $"https://www.curseforge.com/minecraft/mc-mods/{Uri.EscapeDataString(slug)}");
        }

        private ContentVersion parseFile(JsonElement fileElement)
        {
            var modId = fileElement.GetProperty("modId").GetInt32().ToString();
            var fileId = fileElement.GetProperty("id").GetInt32().ToString();
            var hashes = fileElement.TryGetProperty("hashes", out var hashesElement) ? hashesElement.EnumerateArray().ToList() : [];
            var sha1 = hashes.FirstOrDefault(hash => hash.GetProperty("algo").GetInt32() == 1) is { ValueKind: JsonValueKind.Object } shadow
                ? shadow.GetProperty("value").GetString()
                : null;
            var gameVersions = fileElement.TryGetProperty("gameVersions", out var gameVersionsElement) ? gameVersionsElement.EnumerateArray().Select(gameVersionElement => gameVersionElement.GetString()!).ToList() : [];
            var loaders = gameVersions.Where(gameVersion => gameVersion is "Forge" or "Fabric" or "Quilt" or "NeoForge" or "Iris" or "OptiFine")
                .Select(gameVersion => gameVersion.ToLowerInvariant()).ToList();
            // Mods ohne Freigabe für Drittanbieter haben keine Download-URL: der Launcher zeigt dann einen Dialog
            var url = fileElement.TryGetProperty("downloadUrl", out var downloadUrl) && downloadUrl.ValueKind == JsonValueKind.String ? downloadUrl.GetString()! : "";
            var otherDependencies = fileElement.TryGetProperty("dependencies", out var dependencies)
                ? dependencies.EnumerateArray().Select(dependencyElement => new ContentDependency(dependencyElement.GetProperty("modId").GetInt32().ToString(), null,
                    dependencyElement.GetProperty("relationType").GetInt32() switch
                    {
                        3 => DependencyKind.Required,
                        5 => DependencyKind.Incompatible,
                        1 => DependencyKind.Embedded,
                        _ => DependencyKind.Optional
                    })).ToList()
                : [];
            return new ContentVersion(Source, $"{modId}:{fileId}", modId, _textHelper.Clean(fileElement.GetProperty("displayName").GetString(), 120),
                _textHelper.Clean(fileElement.GetProperty("displayName").GetString(), 80),
                fileElement.TryGetProperty("releaseType", out var releaseType) ? releaseType.GetInt32() switch { 2 => "beta", 3 => "alpha", _ => "release" } : "release",
                fileElement.GetProperty("fileDate").GetDateTimeOffset(),
                gameVersions.Where(gameVersion => char.IsDigit(gameVersion[0])).ToList(), loaders,
                [new ContentFile(url, fileElement.GetProperty("fileName").GetString()!, sha1, null, fileElement.GetProperty("fileLength").GetInt64(), true)],
                otherDependencies);
        }

        private string requireNumeric(string id) =>
            id.Length is > 0 and < 12 && id.All(char.IsAsciiDigit) ? id : throw new AxoException("errors.invalidId");

        /// <summary>CurseForge-Fingerprint: Murmur2 (Seed 1) über die Datei ohne Leerraum-Bytes.</summary>
        public uint Fingerprint(ReadOnlySpan<byte> data)
        {
            var filtered = new List<byte>(data.Length);
            foreach (var dataByte in data)
            {
                if (dataByte is not (9 or 10 or 13 or 32))
                {
                    filtered.Add(dataByte);
                }
            }

            return murmur2(filtered.ToArray(), 1);
        }

        private uint murmur2(byte[] data, uint seed)
        {
            const uint murmurMultiplier = 0x5bd1e995;
            const int murmurShift = 24;
            var length = data.Length;
            var hash = seed ^ (uint)length;
            var offset = 0;
            while (length >= 4)
            {
                var block = (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);
                block *= murmurMultiplier;
                block ^= block >> murmurShift;
                block *= murmurMultiplier;
                hash *= murmurMultiplier;
                hash ^= block;
                offset += 4;
                length -= 4;
            }
            switch (length)
            {
                case 3:
                    hash ^= (uint)data[offset + 2] << 16;
                    goto case 2;
                case 2:
                    hash ^= (uint)data[offset + 1] << 8;
                    goto case 1;
                case 1:
                    hash ^= data[offset];
                    hash *= murmurMultiplier;
                    break;
            }
            hash ^= hash >> 13;
            hash *= murmurMultiplier;
            hash ^= hash >> 15;
            return hash;
        }
    }
}
