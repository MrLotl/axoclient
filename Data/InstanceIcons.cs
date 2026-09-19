using System.IO;
using System.Windows.Media.Imaging;

namespace McLauncher;

/// <summary>
/// Bild einer Instanz: ein eigenes Bild (unter ".axoclient/icons") oder automatisch
/// das Vorschaubild der zuletzt gespielten Welt.
/// </summary>
public static class InstanceIcons
{
    public const string FileFilter = "Bilder (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp";

    private static string Dir => Path.Combine(AppState.LauncherDir, "icons");

    public static string? CustomPath(Installation inst) =>
        inst.IconFile is { Length: > 0 } file && File.Exists(Path.Combine(Dir, file)) ? Path.Combine(Dir, file) : null;

    /// <summary>Eigenes Bild, sonst Weltbild, sonst null (dann zeigt die Oberfläche die Loader-Kachel).</summary>
    public static BitmapSource? Load(Installation inst) =>
        (CustomPath(inst) is { } custom ? LoadImage(custom) : null) ?? LoadLatestWorldIcon(inst.GameDir);

    /// <summary>Kopiert ein Bild als eigenes Instanzbild und liefert den neuen Dateinamen.</summary>
    public static string Import(Installation inst, string sourcePath)
    {
        if (LoadImage(sourcePath) == null)
            throw new InvalidOperationException("Die Datei ist kein unterstütztes Bild.");
        Directory.CreateDirectory(Dir);
        // Neuer Name pro Änderung, damit kein altes Bild aus dem Zwischenspeicher angezeigt wird
        var file = $"{inst.Id}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(sourcePath).ToLowerInvariant()}";
        File.Copy(sourcePath, Path.Combine(Dir, file), overwrite: true);
        Remove(inst);
        return file;
    }

    /// <summary>Löscht das eigene Bild (danach wird wieder automatisch das Weltbild verwendet).</summary>
    public static void Remove(Installation inst)
    {
        if (CustomPath(inst) is { } path)
        {
            try { File.Delete(path); } catch { /* evtl. noch angezeigt, egal */ }
        }
    }

    public static BitmapSource? LoadImage(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // Datei nicht gesperrt lassen
            image.DecodePixelWidth = 380;                 // reicht für die große Kachel, spart Speicher
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? LoadLatestWorldIcon(string gameDir)
    {
        var saves = Path.Combine(gameDir, "saves");
        if (!Directory.Exists(saves))
            return null;
        // Minecraft legt beim Spielen ein icon.png im Weltordner ab; level.dat verrät, wann zuletzt gespielt wurde
        var icon = new DirectoryInfo(saves).GetDirectories()
            .Where(d => File.Exists(Path.Combine(d.FullName, "icon.png")))
            .OrderByDescending(d => File.GetLastWriteTime(Path.Combine(d.FullName, "level.dat")))
            .Select(d => Path.Combine(d.FullName, "icon.png"))
            .FirstOrDefault();
        return icon == null ? null : LoadImage(icon);
    }
}
