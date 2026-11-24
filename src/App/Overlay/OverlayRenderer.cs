using System.Text.Json;

namespace AxoClient.App.Overlay
{
    /// <summary>
    /// Zeichnet ein Bild der privaten Widgets aus den Befehlen der Mod (Typ "hud") in ein Pixelbild mit
    /// vormultiplizierter Deckkraft (wie es <c>UpdateLayeredWindow</c> braucht) – nur so groß wie die Widgets selbst.
    /// <para>
    /// Befehle (Koordinaten in Einheiten der Widgets, <c>guiWidth</c> = Breite des ganzen Spielbilds):
    /// <c>{"t":"r", x, y, w, h, s, c}</c> Fläche (Größe × s), <c>{"t":"t", x, y, w, s, c, sh, v}</c> Text in der
    /// Minecraft-Schrift (Schrift-Pixel × s, w = gemessene Breite), Farben als ARGB.
    /// </para>
    /// </summary>
    public class OverlayRenderer
    {
        /// <summary>Das fertige Bild: Pixel und Lage im Spielfenster (Bildpunkte).</summary>
        public sealed record Image(uint[] Pixels, int X, int Y, int Width, int Height);

        private sealed class Canvas
        {
            public readonly uint[] Pixels;
            public readonly int X, Y, Width, Height;

            public Canvas(uint[] pixels, int x, int y, int width, int height)
            {
                Pixels = pixels;
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }
        }

        /// <summary>Zeichnet den Frame für ein Spielfenster von clientWidth × clientHeight Bildpunkten; null = nichts zu sehen.</summary>
        public Image? Render(JsonElement frame, int clientWidth, int clientHeight, McFont? font)
        {
            if (!frame.TryGetProperty("guiWidth", out var guiWidthElement) || !guiWidthElement.TryGetSingle(out var guiWidth) || guiWidth <= 0
                || !frame.TryGetProperty("cmds", out var commands) || commands.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var pixelScale = clientWidth / guiWidth; // Bildpunkte je Einheit

            // Erst die Fläche bestimmen, die überhaupt bemalt wird
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var command in commands.EnumerateArray())
            {
                var x = readNumber(command, "x");
                var y = readNumber(command, "y");
                var scale = readNumber(command, "s", 1);
                var width = readNumber(command, "w") * scale;
                var height = readString(command, "t") == "t" ? 10 * scale : readNumber(command, "h") * scale;
                if (readString(command, "t") == "t")
                {
                    width += 2 * scale; // Schatten und Fett
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x + width);
                maxY = Math.Max(maxY, y + height);
            }
            var left = Math.Max(0, (int)MathF.Floor(minX * pixelScale) - 2);
            var top = Math.Max(0, (int)MathF.Floor(minY * pixelScale) - 2);
            var right = Math.Min(clientWidth, (int)MathF.Ceiling(maxX * pixelScale) + 2);
            var bottom = Math.Min(clientHeight, (int)MathF.Ceiling(maxY * pixelScale) + 2);
            if (right <= left || bottom <= top)
            {
                return null;
            }

            var canvas = new Canvas(new uint[(right - left) * (bottom - top)], left, top, right - left, bottom - top);
            foreach (var command in commands.EnumerateArray())
            {
                var type = readString(command, "t");
                var x = readNumber(command, "x");
                var y = readNumber(command, "y");
                var scale = readNumber(command, "s", 1);
                var color = readColor(command);
                if (type == "r")
                {
                    fill(canvas, x * pixelScale, y * pixelScale, (x + readNumber(command, "w") * scale) * pixelScale, (y + readNumber(command, "h") * scale) * pixelScale, color);
                }
                else if (type == "t" && readString(command, "v") is { Length: > 0 } text)
                {
                    // Wie Minecraft: Textfarben ohne Deckkraft-Anteil gelten als voll deckend
                    if ((color & 0xFC000000) == 0)
                    {
                        color |= 0xFF000000;
                    }

                    if (command.TryGetProperty("sh", out var shadowElement) && shadowElement.ValueKind == JsonValueKind.True)
                    {
                        // Schatten wie Minecraft: ein Schrift-Pixel versetzt, Farbe auf ein Viertel abgedunkelt
                        var shadow = (color & 0xFF000000) | ((color & 0xFCFCFC) >> 2);
                        drawText(canvas, font, text, x + scale, y + scale, scale, pixelScale, shadow);
                    }
                    drawText(canvas, font, text, x, y, scale, pixelScale, color);
                }
            }
            return new Image(canvas.Pixels, left, top, canvas.Width, canvas.Height);
        }

        private string? readString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var valueElement) && valueElement.ValueKind == JsonValueKind.String ? valueElement.GetString() : null;

        /// <summary>Farbe als ganze Zahl lesen (als Kommazahl gingen die unteren Bits verloren).</summary>
        private uint readColor(JsonElement element) =>
            element.TryGetProperty("c", out var colorElement) && colorElement.ValueKind == JsonValueKind.Number && colorElement.TryGetInt64(out var parsed) ? (uint)(parsed & 0xFFFFFFFF) : 0;

        private float readNumber(JsonElement element, string name, float fallback = 0) =>
            element.TryGetProperty(name, out var valueElement) && valueElement.ValueKind == JsonValueKind.Number ? (float)valueElement.GetDouble() : fallback;

        /// <summary>Text: jedes gesetzte Schrift-Pixel als kleine Fläche (scharf wie im Spiel).</summary>
        private void drawText(Canvas canvas, McFont? font, string text, float x, float y, float scale, float pixelScale, uint color)
        {
            var pen = x;
            for (var index = 0; index < text.Length; index += char.IsSurrogatePair(text, index) ? 2 : 1)
            {
                var codepoint = char.IsSurrogate(text[index]) && !char.IsSurrogatePair(text, index) ? '?' : char.ConvertToUtf32(text, index);
                var glyph = font?.Get(codepoint);
                if (glyph != null)
                {
                    var unit = scale * glyph.Scale;
                    var top = y + glyph.Top * scale;
                    for (var glyphY = 0; glyphY < glyph.Height; glyphY++)
                    {
                        for (var glyphX = 0; glyphX < glyph.Width; glyphX++)
                        {
                            var alpha = glyph.Alpha[glyphY * glyph.Width + glyphX];
                            if (alpha == 0)
                            {
                                continue;
                            }

                            var pixelColor = alpha == 255 ? color : ((color >> 24) * alpha / 255 << 24) | (color & 0xFFFFFF);
                            fill(canvas, (pen + glyphX * unit) * pixelScale, (top + glyphY * unit) * pixelScale, (pen + (glyphX + 1) * unit) * pixelScale, (top + (glyphY + 1) * unit) * pixelScale, pixelColor);
                        }
                    }

                    pen += glyph.Advance * scale;
                }
                else
                {
                    var advance = font?.Advance(codepoint) ?? 6;
                    if (codepoint != ' ' && advance > 2)
                    {
                        // Unbekanntes Zeichen: Kästchen, wie Minecraft ohne passende Schrift
                        var width = (advance - 1) * scale;
                        fill(canvas, pen * pixelScale, y * pixelScale, (pen + width) * pixelScale, (y + scale) * pixelScale, color);
                        fill(canvas, pen * pixelScale, (y + 7 * scale) * pixelScale, (pen + width) * pixelScale, (y + 8 * scale) * pixelScale, color);
                        fill(canvas, pen * pixelScale, (y + scale) * pixelScale, (pen + scale) * pixelScale, (y + 7 * scale) * pixelScale, color);
                        fill(canvas, (pen + width - scale) * pixelScale, (y + scale) * pixelScale, (pen + width) * pixelScale, (y + 7 * scale) * pixelScale, color);
                    }
                    pen += advance * scale;
                }
            }
        }

        /// <summary>Fläche von (x0, y0) bis (x1, y1) in Bildpunkten des Spielfensters, gerundet wie die Grafikkarte.</summary>
        private void fill(Canvas canvas, float startX, float startY, float endX, float endY, uint argb)
        {
            var alpha = argb >> 24;
            if (alpha == 0)
            {
                return;
            }

            var left = Math.Max(canvas.X, (int)MathF.Round(startX)) - canvas.X;
            var right = Math.Min(canvas.X + canvas.Width, (int)MathF.Round(endX)) - canvas.X;
            var top = Math.Max(canvas.Y, (int)MathF.Round(startY)) - canvas.Y;
            var bottom = Math.Min(canvas.Y + canvas.Height, (int)MathF.Round(endY)) - canvas.Y;
            if (left >= right || top >= bottom)
            {
                return;
            }
            // Vormultipliziert: Farbe × Deckkraft
            var red = ((argb >> 16) & 0xFF) * alpha / 255;
            var green = ((argb >> 8) & 0xFF) * alpha / 255;
            var blue = (argb & 0xFF) * alpha / 255;
            var inv = 255 - alpha;
            var pixels = canvas.Pixels;
            for (var index = top; index < bottom; index++)
            {
                var row = index * canvas.Width;
                for (var pixelX = left; pixelX < right; pixelX++)
                {
                    var existingPixel = pixels[row + pixelX];
                    if (inv == 0 || existingPixel == 0)
                    {
                        pixels[row + pixelX] = (alpha << 24) | (red << 16) | (green << 8) | blue;
                        continue;
                    }
                    // Über das Vorhandene legen (source over)
                    pixels[row + pixelX] = ((alpha + (existingPixel >> 24) * inv / 255) << 24) | ((red + ((existingPixel >> 16) & 0xFF) * inv / 255) << 16)
                                       | ((green + ((existingPixel >> 8) & 0xFF) * inv / 255) << 8) | (blue + (existingPixel & 0xFF) * inv / 255);
                }
            }
        }
    }
}
