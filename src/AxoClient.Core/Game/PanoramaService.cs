using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using AxoClient.Core.Common;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Game;

/// <summary>
/// Liest das Titelbild-Panorama einer Minecraft-Version aus deren Spieldateien. Neuere Versionen liefern es
/// über den Asset-Index (<c>game/assets/objects</c>), ältere stecken es in die Versions-Jar. Die Bilder werden
/// nicht mit dem Launcher ausgeliefert, sondern nur aus den Dateien angezeigt, die Mojang für die Instanz
/// ohnehin geladen hat.
/// </summary>
public sealed class PanoramaService(AppPaths paths)
{
    private const string AssetName = "minecraft/textures/gui/title/background/panorama_";
    private const long MaxBytes = 8 * 1024 * 1024;

    /// <summary>Kleinere PNGs sind Platzhalter (neuere Jars enthalten nur noch ein winziges Ersatzbild).</summary>
    private const long MinBytes = 2048;

    // Nur Treffer merken: fehlen die Dateien noch, können sie nach dem ersten Start der Instanz da sein
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    /// <summary>PNG einer Würfelseite (0 = Blick nach vorn), oder <c>null</c>, wenn es keine gibt.</summary>
    public byte[]? Get(string version, int face = 0)
    {
        if (!IsValidVersion(version) || face is < 0 or > 5)
            return null;
        var key = version + ":" + face;
        if (_cache.TryGetValue(key, out var hit))
            return hit;
        var bytes = FromAssets(version, face) ?? FromJar(version, face);
        if (bytes != null)
            _cache[key] = bytes;
        return bytes;
    }

    private byte[]? FromAssets(string version, int face)
    {
        var fs = paths.Fs;
        try
        {
            var versionJson = fs.Path.Combine(paths.Versions, version, version + ".json");
            if (!fs.File.Exists(versionJson))
                return null;
            string? indexId;
            using (var doc = JsonDocument.Parse(fs.File.ReadAllText(versionJson)))
            {
                var root = doc.RootElement;
                indexId = root.TryGetProperty("assetIndex", out var ai) && ai.TryGetProperty("id", out var id) ? id.GetString()
                    : root.TryGetProperty("assets", out var a) ? a.GetString() : null;
            }
            if (indexId == null || !IsValidVersion(indexId))
                return null;
            var indexFile = fs.Path.Combine(paths.Assets, "indexes", indexId + ".json");
            if (!fs.File.Exists(indexFile))
                return null;
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
                return null;
            var file = fs.Path.Combine(paths.Assets, "objects", hash[..2], hash);
            if (!fs.File.Exists(file) || fs.FileInfo.New(file).Length is < MinBytes or > MaxBytes)
                return null;
            var bytes = fs.File.ReadAllBytes(file);
            return ImageFormats.IsPng(bytes) ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private byte[]? FromJar(string version, int face)
    {
        var jar = paths.Fs.Path.Combine(paths.Versions, version, version + ".jar");
        if (!paths.Fs.File.Exists(jar))
            return null;
        try
        {
            using var stream = paths.Fs.File.OpenRead(jar);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry("assets/" + AssetName + face + ".png");
            if (entry == null || entry.Length is < MinBytes or > MaxBytes)
                return null;
            using var input = entry.Open();
            using var output = new MemoryStream((int)entry.Length);
            input.CopyTo(output);
            var bytes = output.ToArray();
            return ImageFormats.IsPng(bytes) ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Versionsnamen wie „1.21.4“, „24w14a“ oder „1.20.1-pre2“; nichts, was aus dem Ordner führt.</summary>
    public static bool IsValidVersion(string version) =>
        version.Length is > 0 and <= 64
        && !version.Contains("..", StringComparison.Ordinal)
        && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}
