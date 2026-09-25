using System.IO.Compression;

namespace AxoClient.Import;

public enum DropKind
{
    Unknown,
    SharePackage,
    Modpack,
    Mod,
    ResourcePack,
    Shader,
    World,
    Image,
    InstanceBackup
}

public record DroppedFile(string Path, DropKind Kind)
{
    public string Name => System.IO.Path.GetFileName(Path);

    public string KindText => Kind switch
    {
        DropKind.SharePackage => "AxoClient-Paket",
        DropKind.Modpack => "Modpack",
        DropKind.Mod => "Mod",
        DropKind.ResourcePack => "Ressourcenpaket",
        DropKind.Shader => "Shaderpaket",
        DropKind.World => "Welt",
        DropKind.Image => "Bild",
        DropKind.InstanceBackup => "Instanz-Sicherung",
        _ => "unbekannt"
    };

    public bool CreatesInstance => Kind is DropKind.Modpack or DropKind.SharePackage;

    public ContentType? ContentType => Kind switch
    {
        DropKind.Mod => Content.ContentType.Mod,
        DropKind.ResourcePack => Content.ContentType.ResourcePack,
        DropKind.Shader => Content.ContentType.Shader,
        _ => null
    };
}

public static class DropRouter
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    public static List<DroppedFile> Classify(IEnumerable<string> paths) =>
        paths.Where(File.Exists).Select(p => new DroppedFile(p, KindOf(p))).ToList();

    private static DropKind KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jar" => DropKind.Mod,
        ".mrpack" => DropKind.Modpack,
        ".json" => IsSharePackage(path) ? DropKind.SharePackage : DropKind.Unknown,
        ".zip" => ZipKind(path),
        var extension when ImageExtensions.Contains(extension) => DropKind.Image,
        _ => DropKind.Unknown
    };

    private static bool IsSharePackage(string path)
    {
        try
        {
            return new FileInfo(path).Length <= ShareValidation.MaxFileBytes
                   && ShareJson.DetectKind(File.ReadAllText(path)) != null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static DropKind ZipKind(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var names = zip.Entries.Take(400).Select(e => e.FullName.Replace('\\', '/')).ToList();

            if (InstanceBackup.IsBackupArchive(names))
                return DropKind.InstanceBackup;
            if (names.Any(n => n.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase)))
                return DropKind.World;

            var stripped = names.Concat(names.Select(StripRootFolder)).ToList();
            if (stripped.Any(n => n.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase)))
                return DropKind.ResourcePack;
            if (stripped.Any(n => n.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase)))
                return DropKind.Shader;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }
        return DropKind.Unknown;
    }

    private static string StripRootFolder(string entryPath)
    {
        var slash = entryPath.IndexOf('/');
        return slash >= 0 ? entryPath[(slash + 1)..] : entryPath;
    }

    public static string CopyIntoInstance(Installation inst, DroppedFile file)
    {
        var type = file.ContentType ?? throw new InvalidOperationException("Diese Datei gehört in keinen Inhaltsordner.");
        var folder = inst.ContentDir(type);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, file.Name);
        File.Copy(file.Path, target, overwrite: true);
        return target;
    }
}
