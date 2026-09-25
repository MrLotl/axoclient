using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace AxoClient.Accounts;

public class SkinEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Slim { get; set; }

    [JsonIgnore] public string FilePath { get; set; } = "";
    [JsonIgnore] public BitmapSource? Preview { get; set; }
    [JsonIgnore] public string VariantText => Slim ? "Schmale Arme" : "Breite Arme";
}

public class SkinLibrary
{
    private static string Folder => AppPaths.Skins;
    private static string IndexPath => Path.Combine(Folder, "library.json");

    public List<SkinEntry> Load()
    {
        var entries = JsonFiles.Read<List<SkinEntry>>(IndexPath) ?? [];
        foreach (var entry in entries)
        {
            entry.FilePath = Path.Combine(Folder, entry.Id + ".png");
            try
            {
                entry.Preview = SkinRenderer.RenderFront(File.ReadAllBytes(entry.FilePath), entry.Slim);
            }
            catch (Exception ex)
            {
                ErrorReport.Log($"Vorschau für Skin \"{entry.Name}\" ({entry.FilePath})", ex);
            }
        }
        return entries.Where(e => File.Exists(e.FilePath)).ToList();
    }

    public void Add(byte[] png, string name, bool slim)
    {
        Directory.CreateDirectory(Folder);
        var id = Convert.ToHexString(SHA1.HashData(png))[..12].ToLowerInvariant();
        File.WriteAllBytes(Path.Combine(Folder, id + ".png"), png);

        var entries = Load();
        entries.RemoveAll(e => e.Id == id);
        entries.Insert(0, new SkinEntry { Id = id, Name = name, Slim = slim });
        JsonFiles.Write(IndexPath, entries);
    }

    public void Remove(SkinEntry entry)
    {
        var entries = Load();
        entries.RemoveAll(e => e.Id == entry.Id);
        JsonFiles.Write(IndexPath, entries);
        File.Delete(entry.FilePath);
    }

    public static bool IsValidSkin(byte[] png)
    {
        try
        {
            var image = Images.Decode(png);
            return image.PixelWidth == 64 && image.PixelHeight is 64 or 32;
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Skin-Bild prüfen", ex);
            return false;
        }
    }
}
