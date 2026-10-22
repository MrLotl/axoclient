using System.Collections.Concurrent;
using AxoClient.Core.Common;

namespace AxoClient.App;

/// <summary>
/// Pfade kommen nie aus der UI. Nativ gewählte Dateien und hochgeladene Dateien bekommen eine zufällige Referenz,
/// die UI arbeitet nur mit dieser.
/// </summary>
public sealed class FileRefs(AppPaths paths)
{
    private const long MaxUploadBytes = 200L * 1024 * 1024;
    private readonly ConcurrentDictionary<string, string> _refs = new();

    public string Add(string path)
    {
        var id = Guid.NewGuid().ToString("N")[..16];
        _refs[id] = path;
        return id;
    }

    public string Resolve(string? id) =>
        id != null && _refs.TryGetValue(id, out var path) ? path : throw new AxoException("errors.fileRefInvalid");

    public string NameOf(string id) => Path.GetFileName(Resolve(id));

    /// <summary>Datei aus der UI (Drag &amp; Drop) im temporären Ordner ablegen.</summary>
    public async Task<string> UploadAsync(string name, string base64, CancellationToken ct)
    {
        var clean = Text.Clean(Path.GetFileName(name), 120);
        if (!PathGuard.IsSafeFileName(clean))
            throw new AxoException("errors.invalidFileName");
        if (base64.Length > MaxUploadBytes * 4 / 3 + 4)
            throw new AxoException("errors.fileTooLarge");
        var dir = Path.Combine(paths.Tmp, "uploads", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, clean);
        await File.WriteAllBytesAsync(target, Convert.FromBase64String(base64), ct);
        return Add(target);
    }

    public void CleanUploads()
    {
        var dir = Path.Combine(paths.Tmp, "uploads");
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
