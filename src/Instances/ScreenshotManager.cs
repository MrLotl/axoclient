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

        private string getDirectory(string id) => Path.Combine(_paths.GameDirectory(_textHelper.RequireId(id)), "screenshots");

        public IReadOnlyList<ScreenshotInfo> List(string instanceId)
        {
            var directory = getDirectory(instanceId);
            if (!Directory.Exists(directory))
            {
                return [];
            }

            return new DirectoryInfo(directory).EnumerateFiles()
                .Where(file => file.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                            || file.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file =>
                {
                    (int, int)? size = null;
                    try
                    {
                        var header = new byte[24];
                        using (var stream = file.OpenRead())
                        {
                            stream.ReadExactly(header);
                        }

                        size = _imageFormats.PngSize(header);
                    }
                    catch (Exception exception) when (exception is IOException or EndOfStreamException)
                    {
                    }
                    return new ScreenshotInfo(file.Name, file.LastWriteTime, file.Length,
                        $"/axo/screenshot/{instanceId}/{Uri.EscapeDataString(file.Name)}?v={file.LastWriteTimeUtc.Ticks % 1_000_000_007}",
                        size?.Item1, size?.Item2);
                })
                .ToList();
        }

        public string? PathOf(string instanceId, string file) =>
            _textHelper.IsValidId(instanceId) && _pathGuard.IsSafeFileName(file) && _pathGuard.SafeCombine(getDirectory(instanceId), file) is { } screenshotPath
                                       && File.Exists(screenshotPath)
                ? screenshotPath
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

            var target = _pathGuard.Combine(getDirectory(instanceId), clean);
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
