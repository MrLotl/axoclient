using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.Controls;

public sealed class MinecraftFont
{
    private sealed record Glyph(Geometry? Shape, double Advance);

    private static readonly Dictionary<string, MinecraftFont?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<int, Glyph> _glyphs = [];

    public static MinecraftFont? Load(string? jarPath)
    {
        if (string.IsNullOrEmpty(jarPath) || !File.Exists(jarPath))
            return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(jarPath, out var cached))
                return cached;
            MinecraftFont? font = null;
            try
            {
                font = new MinecraftFont();
                font.Read(jarPath);
                if (font._glyphs.Count == 0)
                    font = null;
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Minecraft-Schrift für das private Overlay lesen", ex);
                font = null;
            }
            Cache[jarPath] = font;
            return font;
        }
    }

    public static string? FindJar(string versionsDir, string? version)
    {
        if (version != null)
        {
            var exact = Path.Combine(versionsDir, version, version + ".jar");
            if (File.Exists(exact))
                return exact;
        }
        if (!Directory.Exists(versionsDir))
            return null;
        return Directory.EnumerateDirectories(versionsDir)
            .Select(dir => Path.Combine(dir, Path.GetFileName(dir) + ".jar"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public bool CanDraw(string text)
    {
        foreach (var rune in text.EnumerateRunes())
            if (!_glyphs.ContainsKey(rune.Value))
                return false;
        return true;
    }

    public void Draw(DrawingContext context, string text, Brush brush, Brush? shadow)
    {
        if (shadow != null)
        {
            context.PushTransform(new TranslateTransform(1, 1));
            DrawRun(context, text, shadow);
            context.Pop();
        }
        DrawRun(context, text, brush);
    }

    private void DrawRun(DrawingContext context, string text, Brush brush)
    {
        double x = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!_glyphs.TryGetValue(rune.Value, out var glyph))
                continue;
            if (glyph.Shape != null)
            {
                context.PushTransform(new TranslateTransform(x, 0));
                context.DrawGeometry(brush, null, glyph.Shape);
                context.Pop();
            }
            x += glyph.Advance;
        }
    }

    private void Read(string jarPath)
    {
        using var jar = ZipFile.OpenRead(jarPath);
        var definition = jar.GetEntry("assets/minecraft/font/include/default.json")
                         ?? jar.GetEntry("assets/minecraft/font/default.json");
        if (definition == null)
            return;

        using (var stream = definition.Open())
        using (var json = JsonDocument.Parse(stream))
        {
            foreach (var provider in json.RootElement.GetProperty("providers").EnumerateArray())
            {
                if (provider.TryGetProperty("type", out var type) && type.GetString() == "bitmap")
                    ReadBitmap(jar, provider);
            }
        }

        _glyphs[' '] = new Glyph(null, 4);
        _glyphs[0x200C] = new Glyph(null, 0);
    }

    private void ReadBitmap(ZipArchive jar, JsonElement provider)
    {
        var file = provider.GetProperty("file").GetString() ?? "";
        var path = file.Contains(':') ? file[(file.IndexOf(':') + 1)..] : file;
        var entry = jar.GetEntry("assets/minecraft/textures/" + path);
        if (entry == null)
            return;
        var height = provider.TryGetProperty("height", out var h) ? h.GetInt32() : 8;
        var ascent = provider.GetProperty("ascent").GetInt32();
        var rows = provider.GetProperty("chars").EnumerateArray().Select(r => r.GetString() ?? "").ToList();
        if (rows.Count == 0)
            return;

        var (pixels, width, imageHeight) = ReadPixels(entry);
        var columns = rows.Max(r => r.EnumerateRunes().Count());
        if (columns == 0)
            return;
        int cellWidth = width / columns, cellHeight = imageHeight / rows.Count;
        var scale = height / (double)cellHeight;
        var top = 7 - ascent;

        for (var row = 0; row < rows.Count; row++)
        {
            var column = 0;
            foreach (var rune in rows[row].EnumerateRunes())
            {
                var cellX = column * cellWidth;
                var cellY = row * cellHeight;
                column++;
                if (rune.Value == 0 || _glyphs.ContainsKey(rune.Value))
                    continue;
                _glyphs[rune.Value] = BuildGlyph(pixels, width, cellX, cellY, cellWidth, cellHeight, scale, top);
            }
        }
    }

    private static Glyph BuildGlyph(byte[] pixels, int stride, int cellX, int cellY, int cellWidth, int cellHeight,
        double scale, double top)
    {
        bool Opaque(int x, int y) => pixels[((cellY + y) * stride + cellX + x) * 4 + 3] > 0;

        var used = 0;
        for (var x = cellWidth - 1; x >= 0 && used == 0; x--)
            for (var y = 0; y < cellHeight; y++)
                if (Opaque(x, y))
                {
                    used = x + 1;
                    break;
                }

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        var any = false;
        using (var context = geometry.Open())
        {
            for (var y = 0; y < cellHeight; y++)
            {
                var x = 0;
                while (x < used)
                {
                    if (!Opaque(x, y))
                    {
                        x++;
                        continue;
                    }
                    var start = x;
                    while (x < used && Opaque(x, y))
                        x++;
                    double left = start * scale, right = x * scale, upper = top + y * scale, lower = top + (y + 1) * scale;
                    context.BeginFigure(new Point(left, upper), true, true);
                    context.LineTo(new Point(right, upper), false, false);
                    context.LineTo(new Point(right, lower), false, false);
                    context.LineTo(new Point(left, lower), false, false);
                    any = true;
                }
            }
        }
        geometry.Freeze();
        return new Glyph(any ? geometry : null, Math.Floor(0.5 + used * scale) + 1);
    }

    private static (byte[] Pixels, int Width, int Height) ReadPixels(ZipArchiveEntry entry)
    {
        using var memory = new MemoryStream();
        using (var stream = entry.Open())
            stream.CopyTo(memory);
        return Images.ReadBgra(memory.ToArray());
    }
}
