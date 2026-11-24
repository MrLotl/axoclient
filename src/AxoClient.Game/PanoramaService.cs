using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AxoClient.Common;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;

namespace AxoClient.Game
{
    /// <summary>
    /// Liest das Titelbild-Panorama einer Minecraft-Version aus deren Spieldateien. Neuere Versionen liefern es
    /// über den Asset-Index (<c>game/assets/objects</c>), ältere stecken es in die Versions-Jar. Die Bilder werden
    /// nicht mit dem Launcher ausgeliefert, sondern nur aus den Dateien angezeigt, die Mojang für die Instanz
    /// ohnehin geladen hat.
    /// <para>Wurde eine Version noch nie gestartet, holt <see cref="Fetch"/> nur das eine Panorama-Bild von Mojangs
    /// Download-Server (Versionsdatei → Asset-Index → Bild, mit SHA1-Prüfung) und legt es im Launcher-Cache ab.</para>
    /// </summary>
    public sealed class PanoramaService : IPanoramaService
    {
        private readonly AppPaths _paths;
        private readonly IImageFormats _imageFormats;
        private readonly IApiClient? _api;
        private readonly IVersionManager? _versions;
        private const string ResourcesHost = "https://resources.download.minecraft.net/";
        private readonly TimeSpan _retryAfterFailure = TimeSpan.FromMinutes(10);

        private sealed record VersionJson([property: JsonPropertyName("assetIndex")] AssetIndexRef? AssetIndex);

        private sealed record AssetIndexRef(string Id, string Url);

        private sealed record AssetIndexJson(Dictionary<string, AssetObject>? Objects);

        private sealed record AssetObject(string Hash, long Size);

        // Laufende Downloads teilen (mehrere Bilder fragen gleichzeitig), Fehlschläge eine Weile merken (z. B. offline)
        private readonly ConcurrentDictionary<string, Task<byte[]?>> _inflight = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _failed = new();
        private const string AssetName = "minecraft/textures/gui/title/background/panorama_";
        private const long MaxBytes = 8 * 1024 * 1024;

        /// <summary>Kleinere PNGs sind Platzhalter (neuere Jars enthalten nur noch ein winziges Ersatzbild).</summary>
        private const long MinBytes = 2048;

        // Nur Treffer merken: fehlen die Dateien noch, können sie nach dem ersten Start der Instanz da sein
        private readonly ConcurrentDictionary<string, byte[]> _cache = new();

        public PanoramaService(
            AppPaths paths,
            IImageFormats imageFormats,
            IApiClient? api = null,
            IVersionManager? versions = null)
        {
            _paths = paths;
            _imageFormats = imageFormats;
            _api = api;
            _versions = versions;
        }

        /// <summary>PNG einer Würfelseite (0 = Blick nach vorn), oder <c>null</c>, wenn es keine gibt.</summary>
        public byte[]? Get(string version, int face = 0)
        {
            if (!IsValidVersion(version) || face is < 0 or > 5)
            {
                return null;
            }

            var key = version + ":" + face;
            if (_cache.TryGetValue(key, out var hit))
            {
                return hit;
            }

            var bytes = fromAssets(version, face) ?? fromJar(version, face) ?? fromCache(version, face);
            if (bytes != null)
            {
                _cache[key] = bytes;
            }

            return bytes;
        }

        /// <summary>
        /// Wie <see cref="Get"/>, lädt aber das Panorama nach, wenn die Version noch nie gestartet wurde.
        /// Ohne Internet oder bei unbekannten Versionen <c>null</c>.
        /// </summary>
        public async Task<byte[]?> Fetch(string version, int face = 0, CancellationToken ct = default)
        {
            if (Get(version, face) is { } local)
            {
                return local;
            }

            if (_api == null || _versions == null || !IsValidVersion(version) || face is < 0 or > 5)
            {
                return null;
            }

            var key = version + ":" + face;
            if (_failed.TryGetValue(key, out var at) && DateTimeOffset.UtcNow - at < _retryAfterFailure)
            {
                return null;
            }

            var task = _inflight.GetOrAdd(key, _ => download(version, face, ct));
            try
            {
                var bytes = await task;
                if (bytes == null)
                {
                    _failed[key] = DateTimeOffset.UtcNow;
                }
                else
                {
                    _cache[key] = bytes;
                }

                return bytes;
            }
            finally
            {
                _inflight.TryRemove(key, out _);
            }
        }

        private async Task<byte[]?> download(string version, int face, CancellationToken ct)
        {
            try
            {
                var url = await _versions!.UrlOf(version, ct);
                if (url == null)
                {
                    return null;
                }

                var index = (await _api!.GetJson<VersionJson>(url, TimeSpan.FromDays(7), ct)).AssetIndex;
                if (index == null)
                {
                    return null;
                }

                var objects = (await _api.GetJson<AssetIndexJson>(index.Url, TimeSpan.FromDays(7), ct)).Objects;
                if (objects == null || !objects.TryGetValue(AssetName + face + ".png", out var obj)
                    || obj.Hash is not { Length: 40 } hash || !hash.All(char.IsAsciiHexDigit) || obj.Size is < MinBytes or > MaxBytes)
                {
                    return null;
                }

                using var response = await _api.Send(() => new HttpRequestMessage(HttpMethod.Get, ResourcesHost + hash[..2] + "/" + hash), ct);
                await _api.EnsureSuccess(response, ct);
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                // Nur genau das Bild, das Mojang im Index nennt
                if (bytes.Length > MaxBytes || !Convert.ToHexStringLower(SHA1.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase)
                    || !_imageFormats.IsPng(bytes))
                {
                    return null;
                }

                var fs = _paths.Fs;
                var file = cacheFile(version, face);
                fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(file)!);
                var temp = file + ".tmp";
                await fs.File.WriteAllBytesAsync(temp, bytes, ct);
                fs.File.Move(temp, file, overwrite: true);
                return bytes;
            }
            catch (Exception ex) when (ex is HttpRequestException or AxoException or IOException or JsonException
                                           or TaskCanceledException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private string cacheFile(string version, int face) =>
            _paths.Fs.Path.Combine(_paths.Cache, "panoramas", version + "_" + face + ".png");

        private byte[]? fromCache(string version, int face)
        {
            var file = cacheFile(version, face);
            try
            {
                if (!_paths.Fs.File.Exists(file) || _paths.Fs.FileInfo.New(file).Length is < MinBytes or > MaxBytes)
                {
                    return null;
                }

                var bytes = _paths.Fs.File.ReadAllBytes(file);
                return _imageFormats.IsPng(bytes) ? bytes : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private byte[]? fromAssets(string version, int face)
        {
            var fs = _paths.Fs;
            try
            {
                var versionJson = fs.Path.Combine(_paths.Versions, version, version + ".json");
                if (!fs.File.Exists(versionJson))
                {
                    return null;
                }

                string? indexId;
                using (var doc = JsonDocument.Parse(fs.File.ReadAllText(versionJson)))
                {
                    var root = doc.RootElement;
                    indexId = root.TryGetProperty("assetIndex", out var ai) && ai.TryGetProperty("id", out var id) ? id.GetString()
                        : root.TryGetProperty("assets", out var a) ? a.GetString() : null;
                }
                if (indexId == null || !IsValidVersion(indexId))
                {
                    return null;
                }

                var indexFile = fs.Path.Combine(_paths.Assets, "indexes", indexId + ".json");
                if (!fs.File.Exists(indexFile))
                {
                    return null;
                }

                string? hash;
                using (var doc = JsonDocument.Parse(fs.File.ReadAllText(indexFile)))
                {
                    hash = doc.RootElement.TryGetProperty("objects", out var objects)
                           && objects.TryGetProperty(AssetName + face + ".png", out var obj)
                           && obj.TryGetProperty("hash", out var h)
                        ? h.GetString()
                        : null;
                }
                if (hash is not { Length: 40 } || !hash.All(char.IsAsciiHexDigit))
                {
                    return null;
                }

                var file = fs.Path.Combine(_paths.Assets, "objects", hash[..2], hash);
                if (!fs.File.Exists(file) || fs.FileInfo.New(file).Length is < MinBytes or > MaxBytes)
                {
                    return null;
                }

                var bytes = fs.File.ReadAllBytes(file);
                return _imageFormats.IsPng(bytes) ? bytes : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
            {
                return null;
            }
        }

        private byte[]? fromJar(string version, int face)
        {
            var jar = _paths.Fs.Path.Combine(_paths.Versions, version, version + ".jar");
            if (!_paths.Fs.File.Exists(jar))
            {
                return null;
            }

            try
            {
                using var stream = _paths.Fs.File.OpenRead(jar);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                var entry = zip.GetEntry("assets/" + AssetName + face + ".png");
                if (entry == null || entry.Length is < MinBytes or > MaxBytes)
                {
                    return null;
                }

                using var input = entry.Open();
                using var output = new MemoryStream((int)entry.Length);
                input.CopyTo(output);
                var bytes = output.ToArray();
                return _imageFormats.IsPng(bytes) ? bytes : null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Versionsnamen wie „1.21.4“, „24w14a“ oder „1.20.1-pre2“; nichts, was aus dem Ordner führt.</summary>
        public bool IsValidVersion(string version) =>
            version.Length is > 0 and <= 64
            && !version.Contains("..", StringComparison.Ordinal)
            && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    }
}
