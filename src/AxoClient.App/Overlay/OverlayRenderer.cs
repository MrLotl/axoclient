using System.Text.Json;

namespace AxoClient.App.Overlay;

/// <summary>
/// Zeichnet ein Bild der privaten Widgets aus den Befehlen der Mod (Typ "hud") in ein Pixelbild mit
/// vormultiplizierter Deckkraft (wie es <c>UpdateLayeredWindow</c> braucht) – nur so groß wie die Widgets selbst.
/// <para>
/// Befehle (Koordinaten in Einheiten der Widgets, <c>guiWidth</c> = Breite des ganzen Spielbilds):
/// <c>{"t":"r", x, y, w, h, s, c}</c> Fläche (Größe × s), <c>{"t":"t", x, y, w, s, c, sh, v}</c> Text in der
/// Minecraft-Schrift (Schrift-Pixel × s, w = gemessene Breite), Farben als ARGB.
/// </para>
/// </summary>
internal static class OverlayRenderer
{
    /// <summary>Das fertige Bild: Pixel und Lage im Spielfenster (Bildpunkte).</summary>
    public sealed record Image(uint[] Pixels, int X, int Y, int Width, int Height);

    private sealed class Canvas(uint[] pixels, int x, int y, int width, int height)
    {
        public readonly uint[] Pixels = pixels;
        public readonly int X = x, Y = y, Width = width, Height = height;
    }

    /// <summary>Zeichnet den Frame für ein Spielfenster von clientWidth × clientHeight Bildpunkten; null = nichts zu sehen.</summary>
    public static Image? Render(JsonElement frame, int clientWidth, int clientHeight, McFont? font)
    {
        if (!frame.TryGetProperty("guiWidth", out var gw) || !gw.TryGetSingle(out var guiWidth) || guiWidth <= 0
            || !frame.TryGetProperty("cmds", out var cmds) || cmds.ValueKind != JsonValueKind.Array)
            return null;
        var k = clientWidth / guiWidth; // Bildpunkte je Einheit

        // Erst die Fläche bestimmen, die überhaupt bemalt wird
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var cmd in cmds.EnumerateArray())
        {
            var x = Num(cmd, "x");
            var y = Num(cmd, "y");
            var s = Num(cmd, "s", 1);
            var w = Num(cmd, "w") * s;
            var h = Str(cmd, "t") == "t" ? 10 * s : Num(cmd, "h") * s;
            if (Str(cmd, "t") == "t")
                w += 2 * s; // Schatten und Fett
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + w);
            maxY = Math.Max(maxY, y + h);
        }
        var left = Math.Max(0, (int)MathF.Floor(minX * k) - 2);
        var top = Math.Max(0, (int)MathF.Floor(minY * k) - 2);
        var right = Math.Min(clientWidth, (int)MathF.Ceiling(maxX * k) + 2);
        var bottom = Math.Min(clientHeight, (int)MathF.Ceiling(maxY * k) + 2);
        if (right <= left || bottom <= top)
            return null;

        var canvas = new Canvas(new uint[(right - left) * (bottom - top)], left, top, right - left, bottom - top);
        foreach (var cmd in cmds.EnumerateArray())
        {
            var type = Str(cmd, "t");
            var x = Num(cmd, "x");
            var y = Num(cmd, "y");
            var s = Num(cmd, "s", 1);
            var color = Color(cmd);
            if (type == "r")
                Fill(canvas, x * k, y * k, (x + Num(cmd, "w") * s) * k, (y + Num(cmd, "h") * s) * k, color);
            else if (type == "t" && Str(cmd, "v") is { Length: > 0 } text)
            {
                // Wie Minecraft: Textfarben ohne Deckkraft-Anteil gelten als voll deckend
                if ((color & 0xFC000000) == 0)
                    color |= 0xFF000000;
                if (cmd.TryGetProperty("sh", out var sh) && sh.ValueKind == JsonValueKind.True)
                {
                    // Schatten wie Minecraft: ein Schrift-Pixel versetzt, Farbe auf ein Viertel abgedunkelt
                    var shadow = (color & 0xFF000000) | ((color & 0xFCFCFC) >> 2);
                    Text(canvas, font, text, x + s, y + s, s, k, shadow);
                }
                Text(canvas, font, text, x, y, s, k, color);
            }
        }
        return new Image(canvas.Pixels, left, top, canvas.Width, canvas.Height);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Farbe als ganze Zahl lesen (als Kommazahl gingen die unteren Bits verloren).</summary>
    private static uint Color(JsonElement e) =>
        e.TryGetProperty("c", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var c) ? (uint)(c & 0xFFFFFFFF) : 0;

    private static float Num(JsonElement e, string name, float fallback = 0) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : fallback;

    /// <summary>Text: jedes gesetzte Schrift-Pixel als kleine Fläche (scharf wie im Spiel).</summary>
    private static void Text(Canvas canvas, McFont? font, string text, float x, float y, float s, float k, uint color)
    {
        var pen = x;
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            var cp = char.IsSurrogate(text[i]) && !char.IsSurrogatePair(text, i) ? '?' : char.ConvertToUtf32(text, i);
            var glyph = font?.Get(cp);
            if (glyph != null)
            {
                var unit = s * glyph.Scale;
                var top = y + glyph.Top * s;
                for (var gy = 0; gy < glyph.Height; gy++)
                    for (var gx = 0; gx < glyph.Width; gx++)
                    {
                        var a = glyph.Alpha[gy * glyph.Width + gx];
                        if (a == 0)
                            continue;
                        var c = a == 255 ? color : ((color >> 24) * a / 255 << 24) | (color & 0xFFFFFF);
                        Fill(canvas, (pen + gx * unit) * k, (top + gy * unit) * k, (pen + (gx + 1) * unit) * k, (top + (gy + 1) * unit) * k, c);
                    }
                pen += glyph.Advance * s;
            }
            else
            {
                var advance = font?.Advance(cp) ?? 6;
                if (cp != ' ' && advance > 2)
                {
                    // Unbekanntes Zeichen: Kästchen, wie Minecraft ohne passende Schrift
                    var w = (advance - 1) * s;
                    Fill(canvas, pen * k, y * k, (pen + w) * k, (y + s) * k, color);
                    Fill(canvas, pen * k, (y + 7 * s) * k, (pen + w) * k, (y + 8 * s) * k, color);
                    Fill(canvas, pen * k, (y + s) * k, (pen + s) * k, (y + 7 * s) * k, color);
                    Fill(canvas, (pen + w - s) * k, (y + s) * k, (pen + w) * k, (y + 7 * s) * k, color);
                }
                pen += advance * s;
            }
        }
    }

    /// <summary>Fläche von (x0, y0) bis (x1, y1) in Bildpunkten des Spielfensters, gerundet wie die Grafikkarte.</summary>
    private static void Fill(Canvas canvas, float x0, float y0, float x1, float y1, uint argb)
    {
        var a = argb >> 24;
        if (a == 0)
            return;
        var left = Math.Max(canvas.X, (int)MathF.Round(x0)) - canvas.X;
        var right = Math.Min(canvas.X + canvas.Width, (int)MathF.Round(x1)) - canvas.X;
        var top = Math.Max(canvas.Y, (int)MathF.Round(y0)) - canvas.Y;
        var bottom = Math.Min(canvas.Y + canvas.Height, (int)MathF.Round(y1)) - canvas.Y;
        if (left >= right || top >= bottom)
            return;
        // Vormultipliziert: Farbe × Deckkraft
        var r = ((argb >> 16) & 0xFF) * a / 255;
        var g = ((argb >> 8) & 0xFF) * a / 255;
        var b = (argb & 0xFF) * a / 255;
        var inv = 255 - a;
        var pixels = canvas.Pixels;
        for (var yy = top; yy < bottom; yy++)
        {
            var row = yy * canvas.Width;
            for (var xx = left; xx < right; xx++)
            {
                var d = pixels[row + xx];
                if (inv == 0 || d == 0)
                {
                    pixels[row + xx] = (a << 24) | (r << 16) | (g << 8) | b;
                    continue;
                }
                // Über das Vorhandene legen (source over)
                pixels[row + xx] = ((a + (d >> 24) * inv / 255) << 24) | ((r + ((d >> 16) & 0xFF) * inv / 255) << 16)
                                   | ((g + ((d >> 8) & 0xFF) * inv / 255) << 8) | (b + (d & 0xFF) * inv / 255);
            }
        }
    }
}
