using AxoClient.Core.Common;

namespace AxoClient.Core.Instances;

public sealed record ScreenshotInfo(string File, DateTimeOffset TakenAt, long SizeBytes, string Url, int? Width, int? Height);

/// <summary>Screenshots einer Instanz: auflisten, umbenennen, löschen.</summary>
public sealed class ScreenshotManager(AppPaths paths)
{
    private string Dir(string id) => Path.Combine(paths.GameDir(Text.RequireId(id)), "screenshots");

    public IReadOnlyList<ScreenshotInfo> List(string instanceId)
    {
        var dir = Dir(instanceId);
        if (!Directory.Exists(dir))
            return [];
        return new DirectoryInfo(dir).EnumerateFiles()
            .Where(f => f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                        || f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f =>
            {
                (int, int)? size = null;
                try
                {
                    var header = new byte[24];
                    using (var s = f.OpenRead())
                        s.ReadExactly(header);
                    size = ImageFormats.PngSize(header);
                }
                catch (Exception ex) when (ex is IOException or EndOfStreamException)
                {
                }
                return new ScreenshotInfo(f.Name, f.LastWriteTime, f.Length,
                    $"/axo/screenshot/{instanceId}/{Uri.EscapeDataString(f.Name)}?v={f.LastWriteTimeUtc.Ticks % 1_000_000_007}",
                    size?.Item1, size?.Item2);
            })
            .ToList();
    }

    public string? PathOf(string instanceId, string file) =>
        Text.IsValidId(instanceId) && PathGuard.IsSafeFileName(file) && PathGuard.SafeCombine(Dir(instanceId), file) is { } p
                                   && File.Exists(p)
            ? p
            : null;

    public void Rename(string instanceId, string file, string newName)
    {
        var source = PathOf(instanceId, file) ?? throw new AxoException("errors.notFound");
        var clean = Text.Clean(newName, 100);
        var extension = Path.GetExtension(file);
        if (!clean.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            clean += extension;
        if (!PathGuard.IsSafeFileName(clean))
            throw new AxoException("errors.invalidFileName");
        var target = PathGuard.Combine(Dir(instanceId), clean);
        if (File.Exists(target))
            throw new AxoException("errors.fileExists");
        File.Move(source, target);
    }

    public void Delete(string instanceId, string file)
    {
        var path = PathOf(instanceId, file) ?? throw new AxoException("errors.notFound");
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
}
