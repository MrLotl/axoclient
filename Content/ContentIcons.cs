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
            var bytes = Directory.Exists(path) ? ReadFromFolder(path) : ReadFromZip(path, type);
            if (bytes == null)
                return null;
            var image = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 64;
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

    private static byte[]? ReadFromFolder(string folder)
    {
        var file = Path.Combine(folder, "pack.png");
        return File.Exists(file) ? File.ReadAllBytes(file) : null;
    }

    private static byte[]? ReadFromZip(string path, ContentType type)
    {
        using var zip = ZipFile.OpenRead(path);
        if (type != ContentType.Mod)
            return Read(zip, "pack.png");

        foreach (var candidate in IconPathsOfMod(zip))
            if (Read(zip, candidate) is { } bytes)
                return bytes;
        return null;
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
