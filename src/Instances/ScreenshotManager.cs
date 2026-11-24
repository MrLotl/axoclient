using AxoClient.Common;
using AxoClient.DataModel.Instances;
using AxoClient.Instances.Contract;

namespace AxoClient.Instances
{
    /// <summary>Screenshots einer Instanz: auflisten, umbenennen, löschen.</summary>
    public sealed class ScreenshotManager : IScreenshotManager
    {
        private readonly AppPaths _paths;
        private readonly ITextHelper _textHelper;
        private readonly IPathGuard _pathGuard;
        private readonly IImageFormats _imageFormats;

        public ScreenshotManager(AppPaths paths, ITextHelper textHelper, IPathGuard pathGuard, IImageFormats imageFormats)
        {
            _paths = paths;
            _textHelper = textHelper;
            _pathGuard = pathGuard;
            _imageFormats = imageFormats;
        }

        private string getDir(string id) => Path.Combine(_paths.GameDir(_textHelper.RequireId(id)), "screenshots");

        public IReadOnlyList<ScreenshotInfo> List(string instanceId)
        {
            var dir = getDir(instanceId);
            if (!Directory.Exists(dir))
            {
                return [];
            }

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
                        {
                            s.ReadExactly(header);
                        }

                        size = _imageFormats.PngSize(header);
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
            _textHelper.IsValidId(instanceId) && _pathGuard.IsSafeFileName(file) && _pathGuard.SafeCombine(getDir(instanceId), file) is { } p
                                       && File.Exists(p)
                ? p
                : null;

        public void Rename(string instanceId, string file, string newName)
        {
            var source = PathOf(instanceId, file) ?? throw new AxoException("errors.notFound");
            var clean = _textHelper.Clean(newName, 100);
            var extension = Path.GetExtension(file);
            if (!clean.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                clean += extension;
            }

            if (!_pathGuard.IsSafeFileName(clean))
            {
                throw new AxoException("errors.invalidFileName");
            }

            var target = _pathGuard.Combine(getDir(instanceId), clean);
            if (File.Exists(target))
            {
                throw new AxoException("errors.fileExists");
            }

            File.Move(source, target);
        }

        public void Delete(string instanceId, string file)
        {
            var path = PathOf(instanceId, file) ?? throw new AxoException("errors.notFound");
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
    }
}
