using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace McLauncher;

/// <summary>Liest das Profilbild direkt aus einer Mod-, Ressourcenpaket- oder Shader-Datei (ohne Netzwerk).</summary>
public static class ContentIcons
{
    public static BitmapSource? Load(string path, ContentType type)
    {
        try
        {
            return FromBytes(Directory.Exists(path) ? ReadFromFolder(path) : ReadFromZip(path, type));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Bild aus Rohdaten (z.B. vom Anbieter heruntergeladen); null, wenn das Format nicht lesbar ist.</summary>
    public static BitmapSource? FromBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return null;
        try
        {
            var image = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 128; // reicht auch für die Kacheln, spart aber Speicher bei großen Bildern
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Namen, unter denen Pakete und Shader ihr Bild ablegen.</summary>
    private static readonly string[] PackIconNames = ["pack.png", "icon.png", "preview.png", "screenshot.png", "thumbnail.png"];

    private static byte[]? ReadFromFolder(string folder)
    {
        foreach (var name in PackIconNames)
        {
            var file = Path.Combine(folder, name);
            if (File.Exists(file))
                return File.ReadAllBytes(file);
        }
        return null;
    }

    private static byte[]? ReadFromZip(string path, ContentType type)
    {
        using var zip = ZipFile.OpenRead(path);
        if (type == ContentType.Mod)
        {
            foreach (var candidate in IconPathsOfMod(zip))
                if (Read(zip, candidate) is { } bytes)
                    return bytes;
            return null;
        }

        foreach (var candidate in IconPathsOfPack(zip))
            if (Read(zip, candidate) is { } bytes)
                return bytes;
        return null;
    }

    /// <summary>
    /// Bildpfade in einem Paket oder Shader. Viele Shader-Archive haben einen Unterordner
    /// (z.B. "ComplementaryShaders/shaders/..."), daher wird auch eine Ebene tiefer gesucht.
    /// </summary>
    private static IEnumerable<string> IconPathsOfPack(ZipArchive zip)
    {
        foreach (var name in PackIconNames)
            yield return name;

        var nested = zip.Entries
            .Select(e => e.FullName)
            .Where(full => PackIconNames.Any(name => full.EndsWith('/' + name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(full => full.Count(c => c == '/'))
            .ToList();
        foreach (var full in nested)
            yield return full;
    }

    /// <summary>Mögliche Icon-Pfade laut fabric.mod.json / quilt.mod.json / mods.toml.</summary>
    private static IEnumerable<string> IconPathsOfMod(ZipArchive zip)
    {
        foreach (var manifest in new[] { "fabric.mod.json", "quilt.mod.json" })
        {
            if (zip.GetEntry(manifest) is not { } entry)
                continue;
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (manifest == "quilt.mod.json" && root.TryGetProperty("quilt_loader", out var loader)
                && loader.TryGetProperty("metadata", out var meta))
                root = meta;
            if (!root.TryGetProperty("icon", out var icon))
                continue;
            if (icon.ValueKind == JsonValueKind.String)
                yield return icon.GetString()!;
            else if (icon.ValueKind == JsonValueKind.Object)
                // Größte angebotene Größe zuerst
                foreach (var size in icon.EnumerateObject().OrderByDescending(p => int.TryParse(p.Name, out var n) ? n : 0))
                    if (size.Value.GetString() is { } p)
                        yield return p;
        }

        foreach (var toml in new[] { "META-INF/mods.toml", "META-INF/neoforge.mods.toml" })
        {
            if (zip.GetEntry(toml) is not { } entry)
                continue;
            using var reader = new StreamReader(entry.Open());
            var match = Regex.Match(reader.ReadToEnd(), @"logoFile\s*=\s*""([^""]+)""");
            if (match.Success)
                yield return match.Groups[1].Value;
        }

        yield return "pack.png";
        yield return "logo.png";
    }

    private static byte[]? Read(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name.TrimStart('/').Replace('\\', '/'));
        if (entry == null)
            return null;
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
