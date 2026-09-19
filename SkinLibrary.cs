using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace McLauncher;

/// <summary>Ein gespeicherter Skin in der lokalen Bibliothek.</summary>
public class SkinEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Slim { get; set; }

    [JsonIgnore] public string FilePath { get; set; } = "";
    [JsonIgnore] public BitmapSource? Preview { get; set; }
    [JsonIgnore] public string VariantText => Slim ? "Schmale Arme" : "Breite Arme";
}

/// <summary>Lokale Skin-Sammlung unter ".axoclient/skins", damit man zwischen Skins wechseln kann.</summary>
public class SkinLibrary(string launcherDir)
{
    private string Folder => Path.Combine(launcherDir, "skins");
    private string IndexPath => Path.Combine(Folder, "library.json");

    public List<SkinEntry> Load()
    {
        List<SkinEntry> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<SkinEntry>>(File.ReadAllText(IndexPath)) ?? [];
        }
        catch
        {
            entries = [];
        }

        foreach (var entry in entries)
        {
            entry.FilePath = Path.Combine(Folder, entry.Id + ".png");
            try
            {
                entry.Preview = SkinRenderer.RenderFront(File.ReadAllBytes(entry.FilePath), entry.Slim);
            }
            catch
            {
                entry.Preview = null; // Datei fehlt oder ist beschädigt
            }
        }
        return entries.Where(e => File.Exists(e.FilePath)).ToList();
    }

    /// <summary>Speichert einen Skin; derselbe Skin (gleicher Inhalt) wird nur aktualisiert, nicht doppelt angelegt.</summary>
    public void Add(byte[] png, string name, bool slim)
    {
        Directory.CreateDirectory(Folder);
        var id = Convert.ToHexString(SHA1.HashData(png))[..12].ToLowerInvariant();
        File.WriteAllBytes(Path.Combine(Folder, id + ".png"), png);

        var entries = Load();
        entries.RemoveAll(e => e.Id == id);
        entries.Insert(0, new SkinEntry { Id = id, Name = name, Slim = slim });
        Save(entries);
    }

    public void Remove(SkinEntry entry)
    {
        var entries = Load();
        entries.RemoveAll(e => e.Id == entry.Id);
        Save(entries);
        File.Delete(entry.FilePath);
    }

    private void Save(List<SkinEntry> entries) =>
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Minecraft akzeptiert nur 64x64- und alte 64x32-Skins.</summary>
    public static bool IsValidSkin(byte[] png)
    {
        try
        {
            using var ms = new MemoryStream(png);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            return frame.PixelWidth == 64 && frame.PixelHeight is 64 or 32;
        }
        catch
        {
            return false;
        }
    }
}
