using System.Windows.Media.Imaging;

namespace AxoClient.Instances;

public static class InstanceIcons
{
    public const string FileFilter = "Bilder (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp";

    private const int DecodeWidth = 380;

    public static string? CustomPath(Installation inst) =>
        inst.IconFile is { Length: > 0 } file && File.Exists(Path.Combine(AppPaths.Icons, file))
            ? Path.Combine(AppPaths.Icons, file)
            : null;

    public static BitmapSource? Load(Installation inst) =>
        (CustomPath(inst) is { } custom ? LoadImage(custom) : null) ?? LoadLatestWorldIcon(inst);

    public static BitmapSource? LoadImage(string path) => Images.FromFile(path, DecodeWidth);

    public static void SetCustom(Installation inst, string sourcePath)
    {
        if (LoadImage(sourcePath) == null)
            throw new InvalidOperationException("Die Datei ist kein unterstütztes Bild.");
        Directory.CreateDirectory(AppPaths.Icons);
        var file = $"{inst.Id}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(sourcePath).ToLowerInvariant()}";
        File.Copy(sourcePath, Path.Combine(AppPaths.Icons, file), overwrite: true);
        Remove(inst);
        inst.IconFile = file;
    }

    public static void Remove(Installation inst)
    {
        if (CustomPath(inst) is { } path)
            FileOps.TryDelete(path);
        inst.IconFile = null;
    }

    public static BitmapSource? LoadLatestWorldIcon(Installation inst)
    {
        if (!Directory.Exists(inst.SavesDir))
            return null;
        var icon = new DirectoryInfo(inst.SavesDir).GetDirectories()
            .Where(d => File.Exists(Path.Combine(d.FullName, "icon.png")))
            .OrderByDescending(d => File.GetLastWriteTime(Path.Combine(d.FullName, "level.dat")))
            .Select(d => Path.Combine(d.FullName, "icon.png"))
            .FirstOrDefault();
        return icon == null ? null : LoadImage(icon);
    }
}
