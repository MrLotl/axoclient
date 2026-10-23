using System.IO.Compression;
using System.Text.Json;
using AxoClient.Core.Common;

namespace AxoClient.App.Overlay;

/// <summary>
/// Die Minecraft-Schrift aus der Minecraft-Jar der Instanz: die Bitmap-Zeichensätze aus
/// <c>assets/minecraft/font/default.json</c> (samt Verweisen), genau wie Minecraft sie zeichnet. So sehen private
/// Widgets im Overlay-Fenster aus wie im Spiel. Zeichen, die es dort nicht gibt (Unifont), werden als Kästchen gezeigt.
/// </summary>
internal sealed class McFont
{
    /// <summary>Ein Zeichen: Pixel (zeilenweise, Deckkraft 0–255), Größe in Bildpunkten, Maßstab, Oberkante, Vorschub.</summary>
    public sealed record Glyph(byte[] Alpha, int Width, int Height, float Scale, float Top, float Advance);

    private readonly Dictionary<int, Glyph> _glyphs = new();
    private readonly Dictionary<int, float> _spaces = new();

    private McFont()
    {
    }

    /// <summary>Lädt die Schrift aus der Jar oder <c>null</c>, wenn sie dort nicht lesbar ist.</summary>
    public static McFont? Load(string jarPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            var font = new McFont();
            font.LoadDefinition(zip, "minecraft:default", 0);
            return font._glyphs.Count > 0 ? font : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Warn("Minecraft-Schrift für das private Overlay nicht lesbar: " + jarPath, ex);
            return null;
        }
    }

    public Glyph? Get(int codepoint) => _glyphs.GetValueOrDefault(codepoint);

    /// <summary>Vorschub eines Zeichens in Schrift-Pixeln (wie <c>Font.width</c> in Minecraft).</summary>
    public float Advance(int codepoint)
    {
        if (_spaces.TryGetValue(codepoint, out var space))
            return space;
        return _glyphs.TryGetValue(codepoint, out var g) ? g.Advance : 6;
    }

    private static (string Ns, string Path) Split(string id)
    {
        var colon = id.IndexOf(':');
        return colon < 0 ? ("minecraft", id) : (id[..colon], id[(colon + 1)..]);
    }

    private void LoadDefinition(ZipArchive zip, string id, int depth)
    {
        if (depth > 4)
            return;
        var (ns, path) = Split(id);
        var entry = zip.GetEntry($"assets/{ns}/font/{path}.json");
        if (entry == null)
            return;
        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Array)
            return;
        foreach (var provider in providers.EnumerateArray())
        {
            var type = provider.TryGetProperty("type", out var t) ? t.GetString() : null;
            // "uniform" ist die Unicode-Schrift (Einstellung "Unicode-Schrift erzwingen") – die nutzt das Spiel normal nicht
            if (provider.TryGetProperty("filter", out var filter) && filter.TryGetProperty("uniform", out var uniform)
                && uniform.ValueKind == JsonValueKind.True)
                continue;
            switch (type)
            {
                case "reference" when provider.TryGetProperty("id", out var refId) && refId.GetString() is { } r:
                    LoadDefinition(zip, r, depth + 1);
                    break;
                case "space" when provider.TryGetProperty("advances", out var advances):
                    foreach (var a in advances.EnumerateObject())
                        if (a.Name.Length > 0 && a.Value.TryGetSingle(out var adv))
                            _spaces.TryAdd(char.ConvertToUtf32(a.Name, 0), adv);
                    break;
                case "bitmap":
                    LoadBitmap(zip, provider);
                    break;
            }
        }
    }

    private void LoadBitmap(ZipArchive zip, JsonElement provider)
    {
        if (!provider.TryGetProperty("file", out var fileProp) || fileProp.GetString() is not { } file
            || !provider.TryGetProperty("chars", out var charsProp) || charsProp.ValueKind != JsonValueKind.Array)
            return;
        var height = provider.TryGetProperty("height", out var h) && h.TryGetInt32(out var hv) ? hv : 8;
        var ascent = provider.TryGetProperty("ascent", out var a) && a.TryGetInt32(out var av) ? av : 7;
        var (ns, path) = Split(file);
        var entry = zip.GetEntry($"assets/{ns}/textures/{path}");
        if (entry == null)
            return;
        var rows = charsProp.EnumerateArray().Select(r => r.GetString() ?? "").Select(Codepoints).ToList();
        if (rows.Count == 0 || rows[0].Length == 0)
            return;

        int imgW, imgH;
        byte[] alpha;
        using (var stream = entry.Open())
        using (var copy = new MemoryStream())
        {
            stream.CopyTo(copy);
            copy.Position = 0;
            using var bitmap = new System.Drawing.Bitmap(copy);
            imgW = bitmap.Width;
            imgH = bitmap.Height;
            alpha = new byte[imgW * imgH];
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, imgW, imgH), System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[imgW];
                for (var y = 0; y < imgH; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, imgW);
                    for (var x = 0; x < imgW; x++)
                        alpha[y * imgW + x] = (byte)((uint)row[x] >> 24);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        var cellW = imgW / rows[0].Length;
        var cellH = imgH / rows.Count;
        if (cellW <= 0 || cellH <= 0)
            return;
        var scale = height / (float)cellH;
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Length && c * cellW < imgW; c++)
            {
                var cp = rows[r][c];
                if (cp == 0 || _glyphs.ContainsKey(cp))
                    continue;
                var glyph = new byte[cellW * cellH];
                var width = 0;
                for (var y = 0; y < cellH; y++)
                    for (var x = 0; x < cellW; x++)
                    {
                        var value = alpha[(r * cellH + y) * imgW + c * cellW + x];
                        glyph[y * cellW + x] = value;
                        if (value != 0)
                            width = Math.Max(width, x + 1);
                    }
                // Wie Minecraft: Breite = rechteste gesetzte Spalte, Vorschub = Breite × Maßstab (gerundet) + 1
                _glyphs[cp] = new Glyph(glyph, cellW, cellH, scale, 7 - ascent, (int)(0.5F + width * scale) + 1);
            }
    }

    private static int[] Codepoints(string text)
    {
        var list = new List<int>();
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
            list.Add(char.ConvertToUtf32(text, i));
        return list.ToArray();
    }
}
