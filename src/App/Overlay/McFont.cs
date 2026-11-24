using System.IO.Compression;
using System.Text.Json;

namespace AxoClient.App.Overlay
{
    /// <summary>
    /// Die Minecraft-Schrift aus der Minecraft-Jar der Instanz: die Bitmap-Zeichensätze aus
    /// <c>assets/minecraft/font/default.json</c> (samt Verweisen), genau wie Minecraft sie zeichnet. So sehen private
    /// Widgets im Overlay-Fenster aus wie im Spiel. Zeichen, die es dort nicht gibt (Unifont), werden als Kästchen gezeigt.
    /// </summary>
    public sealed class McFont
    {
        /// <summary>Ein Zeichen: Pixel (zeilenweise, Deckkraft 0–255), Größe in Bildpunkten, Maßstab, Oberkante, Vorschub.</summary>
        public sealed record Glyph(byte[] Alpha, int Width, int Height, float Scale, float Top, float Advance);

        private readonly Dictionary<int, Glyph> _glyphs = new();
        private readonly Dictionary<int, float> _spaces = new();

        /// <summary>Liest die Zeichensätze aus der Minecraft-Jar.</summary>
        public McFont(ZipArchive zip)
        {
            loadDefinition(zip, "minecraft:default", 0);
        }

        /// <summary>Keine Zeichen gefunden?</summary>
        public bool IsEmpty => _glyphs.Count == 0;

        public Glyph? Get(int codepoint) => _glyphs.GetValueOrDefault(codepoint);

        /// <summary>Vorschub eines Zeichens in Schrift-Pixeln (wie <c>Font.width</c> in Minecraft).</summary>
        public float Advance(int codepoint)
        {
            if (_spaces.TryGetValue(codepoint, out var space))
            {
                return space;
            }

            return _glyphs.TryGetValue(codepoint, out var value) ? value.Advance : 6;
        }

        private (string Ns, string Path) split(string id)
        {
            var colon = id.IndexOf(':');
            return colon < 0 ? ("minecraft", id) : (id[..colon], id[(colon + 1)..]);
        }

        private void loadDefinition(ZipArchive zip, string id, int depth)
        {
            if (depth > 4)
            {
                return;
            }

            var (namespaceName, path) = split(id);
            var entry = zip.GetEntry($"assets/{namespaceName}/font/{path}.json");
            if (entry == null)
            {
                return;
            }

            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var provider in providers.EnumerateArray())
            {
                var type = provider.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                // "uniform" ist die Unicode-Schrift (Einstellung "Unicode-Schrift erzwingen") – die nutzt das Spiel normal nicht
                if (provider.TryGetProperty("filter", out var filter) && filter.TryGetProperty("uniform", out var uniform)
                    && uniform.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                switch (type)
                {
                    case "reference" when provider.TryGetProperty("id", out var referenceId) && referenceId.GetString() is { } referencedId:
                        loadDefinition(zip, referencedId, depth + 1);
                        break;
                    case "space" when provider.TryGetProperty("advances", out var advances):
                        foreach (var property in advances.EnumerateObject())
                        {
                            if (property.Name.Length > 0 && property.Value.TryGetSingle(out var adv))
                            {
                                _spaces.TryAdd(char.ConvertToUtf32(property.Name, 0), adv);
                            }
                        }

                        break;
                    case "bitmap":
                        loadBitmap(zip, provider);
                        break;
                }
            }
        }

        private void loadBitmap(ZipArchive zip, JsonElement provider)
        {
            if (!provider.TryGetProperty("file", out var fileProperty) || fileProperty.GetString() is not { } file
                || !provider.TryGetProperty("chars", out var charsProperty) || charsProperty.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var height = provider.TryGetProperty("height", out var heightElement) && heightElement.TryGetInt32(out var parsed) ? parsed : 8;
            var ascent = provider.TryGetProperty("ascent", out var ascentElement) && ascentElement.TryGetInt32(out var ascentValue) ? ascentValue : 7;
            var (namespaceName, path) = split(file);
            var entry = zip.GetEntry($"assets/{namespaceName}/textures/{path}");
            if (entry == null)
            {
                return;
            }

            var rows = charsProperty.EnumerateArray().Select(rowElement => rowElement.GetString() ?? "").Select(codepoints).ToList();
            if (rows.Count == 0 || rows[0].Length == 0)
            {
                return;
            }

            int imageWidth, imageHeight;
            byte[] alpha;
            using (var stream = entry.Open())
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                copy.Position = 0;
                using var bitmap = new System.Drawing.Bitmap(copy);
                imageWidth = bitmap.Width;
                imageHeight = bitmap.Height;
                alpha = new byte[imageWidth * imageHeight];
                var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, imageWidth, imageHeight), System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[imageWidth];
                    for (var y = 0; y < imageHeight; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, imageWidth);
                        for (var x = 0; x < imageWidth; x++)
                        {
                            alpha[y * imageWidth + x] = (byte)((uint)row[x] >> 24);
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }

            var cellW = imageWidth / rows[0].Length;
            var cellH = imageHeight / rows.Count;
            if (cellW <= 0 || cellH <= 0)
            {
                return;
            }

            var scale = height / (float)cellH;
            for (var index = 0; index < rows.Count; index++)
            {
                for (var column = 0; column < rows[index].Length && column * cellW < imageWidth; column++)
                {
                    var codepoint = rows[index][column];
                    if (codepoint == 0 || _glyphs.ContainsKey(codepoint))
                    {
                        continue;
                    }

                    var glyph = new byte[cellW * cellH];
                    var width = 0;
                    for (var y = 0; y < cellH; y++)
                    {
                        for (var x = 0; x < cellW; x++)
                        {
                            var value = alpha[(index * cellH + y) * imageWidth + column * cellW + x];
                            glyph[y * cellW + x] = value;
                            if (value != 0)
                            {
                                width = Math.Max(width, x + 1);
                            }
                        }
                    }
                    // Wie Minecraft: Breite = rechteste gesetzte Spalte, Vorschub = Breite × Maßstab (gerundet) + 1
                    _glyphs[codepoint] = new Glyph(glyph, cellW, cellH, scale, 7 - ascent, (int)(0.5F + width * scale) + 1);
                }
            }
        }

        private int[] codepoints(string text)
        {
            var list = new List<int>();
            for (var index = 0; index < text.Length; index += char.IsSurrogatePair(text, index) ? 2 : 1)
            {
                list.Add(char.ConvertToUtf32(text, index));
            }

            return list.ToArray();
        }
    }
}
