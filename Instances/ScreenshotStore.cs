using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace AxoClient.Instances;

public class ScreenshotInfo : INotifyPropertyChanged
{
    public required string FullPath { get; init; }
    public required DateTime Taken { get; init; }
    public required long SizeBytes { get; init; }

    public string FileName => Path.GetFileName(FullPath);
    public string TakenText => Taken.ToString("dd.MM.yyyy HH:mm");
    public string Details => $"{TakenText}  ·  {Formats.Size(SizeBytes)}";

    private BitmapSource? _thumbnail;

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ScreenshotStore(Installation inst)
{
    private const int ThumbnailWidth = 320;
    private const int MaxNameLength = 80;

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg"];

    public string Dir => inst.ScreenshotsDir;

    public Task<List<ScreenshotInfo>> LoadAsync() => Task.Run(() =>
    {
        try
        {
            return Directory.Exists(Dir)
                ? new DirectoryInfo(Dir).GetFiles()
                    .Where(f => Extensions.Contains(f.Extension.ToLowerInvariant()))
                    .Select(f => new ScreenshotInfo { FullPath = f.FullName, Taken = f.LastWriteTime, SizeBytes = f.Length })
                    .OrderByDescending(s => s.Taken)
                    .ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new List<ScreenshotInfo>();
        }
    });

    public static async Task LoadThumbnailsAsync(IReadOnlyList<ScreenshotInfo> items, CancellationToken cancel)
    {
        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested)
                return;
            BitmapSource? image;
            try
            {
                image = await Task.Run(() => Images.FromFile(item.FullPath, ThumbnailWidth), cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (cancel.IsCancellationRequested)
                return;
            item.Thumbnail = image;
        }
    }

    public static string CleanName(string name) => Sanitize.StripFileName(name, MaxNameLength);

    public string Rename(ScreenshotInfo shot, string newName)
    {
        var clean = CleanName(newName);
        if (clean.Length == 0)
            throw new InvalidOperationException("Der Name darf nicht leer sein.");

        var target = Path.Combine(Dir, clean + Path.GetExtension(shot.FullPath));
        if (string.Equals(target, shot.FullPath, StringComparison.OrdinalIgnoreCase))
            return shot.FullPath;
        if (File.Exists(target))
            throw new InvalidOperationException($"Es gibt schon ein Bild mit dem Namen \"{Path.GetFileName(target)}\".");
        File.Move(shot.FullPath, target);
        return target;
    }

    public static void CopyToClipboard(ScreenshotInfo shot) =>
        System.Windows.Clipboard.SetImage(Images.FromFile(shot.FullPath)
                                          ?? throw new InvalidOperationException("Das Bild lässt sich nicht öffnen."));
}
