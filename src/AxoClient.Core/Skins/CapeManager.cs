using AxoClient.Core.Accounts;
using AxoClient.Core.Common;

namespace AxoClient.Core.Skins;

public enum CapeSource
{
    Editor,
    Upload
}

public enum CapeStatus
{
    Local,
    Pending,
    Approved,
    Rejected
}

public sealed class CapeItem : ILibraryItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";

    /// <summary>Auflösung der Vorderseite im Editor, z. B. "20x32".</summary>
    public string Resolution { get; set; } = "10x16";
    public CapeSource Source { get; set; } = CapeSource.Editor;

    /// <summary>Nur lokal, eingereicht, freigegeben oder abgelehnt.</summary>
    public CapeStatus Status { get; set; } = CapeStatus.Local;
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CapeLibraryDocument : ILibraryDocument<CapeItem>
{
    public int FormatVersion { get; set; } = 1;
    public List<CapeItem> Capes { get; set; } = [];

    List<CapeItem> ILibraryDocument<CapeItem>.Items => Capes;
}

/// <summary><c>capes/&lt;id&gt;.edit.json</c>: Zustand des Editors, damit man später weiterarbeiten kann.</summary>
public sealed class CapeEditState : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public int Width { get; set; } = 10;
    public int Height { get; set; } = 16;
    public CapeImport? Import { get; set; }

    /// <summary>Pixel der Vorderseite als Hex-Farben (Zeilen von oben), "" = transparent.</summary>
    public List<string>? Pixels { get; set; }
}

public sealed record CapeImport(string File, double X, double Y, double Scale);

public sealed record CapeView(string Id, string Name, string Resolution, CapeSource Source, CapeStatus Status, string? Reason, string Url,
    bool Active, bool HasEditState);

/// <summary>Eigene Capes: lokal erstellen (Editor oder Datei), optional zur Veröffentlichung einreichen.</summary>
public sealed class CapeManager(AppPaths paths, AccountManager accounts, EventHub events)
{
    private readonly LibraryStore<CapeLibraryDocument, CapeItem> _library = new(paths, paths.Capes, paths.CapeLibrary);

    public async Task<IReadOnlyList<CapeView>> ListAsync(CancellationToken ct = default)
    {
        var active = accounts.Active?.Capes.Axo;
        return (await _library.ListAsync(ct)).Select(c => new CapeView(c.Id, c.Name, c.Resolution, c.Source, c.Status, c.Reason,
                $"/axo/cape-file/{c.Id}", active is { Source: AxoCapeSource.Custom } && active.Id == c.Id,
                File.Exists(EditPath(c.Id))))
            .ToList();
    }

    public string? FilePath(string id) => _library.FilePathById(id);

    private string EditPath(string id) => Path.Combine(paths.Capes, Text.RequireId(id) + ".edit.json");

    public async Task<CapeView> SaveAsync(string? id, string name, byte[] png, CapeSource source, CapeEditState? edit, CancellationToken ct)
    {
        if (!SkinFiles.IsValidCape(png))
            throw new AxoException("capes.invalidImage");
        var size = Instances.ImageFormats.PngSize(png)!.Value;
        var scale = size.Width / 64;
        var newId = id is { } existing && Text.IsValidId(existing) ? existing : Text.NewId(name);
        var old = await _library.GetAsync(newId, ct);
        var item = new CapeItem
        {
            Id = newId,
            Name = Text.Clean(name, 40) is { Length: > 0 } n ? n : "Cape",
            File = newId + ".png",
            Resolution = $"{10 * scale}x{16 * scale}",
            Source = source,
            // Geändert = wieder nur lokal, eine alte Freigabe gilt nicht für das neue Bild
            Status = CapeStatus.Local,
            CreatedAt = old?.CreatedAt ?? DateTimeOffset.UtcNow
        };
        await _library.AddAsync(item, png, ct);
        if (edit != null)
            await new JsonStore<CapeEditState>(paths.Fs, EditPath(newId)).SaveAsync(edit, ct);
        events.Publish("capes.changed");
        return (await ListAsync(ct)).First(c => c.Id == newId);
    }

    public Task<CapeEditState> LoadEditStateAsync(string id, CancellationToken ct) =>
        new JsonStore<CapeEditState>(paths.Fs, EditPath(id)).LoadAsync(ct);

    public async Task RemoveAsync(string id, CancellationToken ct)
    {
        await _library.RemoveAsync(id, ct);
        if (File.Exists(EditPath(id)))
            File.Delete(EditPath(id));
        events.Publish("capes.changed");
    }

    public async Task SetStatusAsync(string id, CapeStatus status, string? reason, CancellationToken ct = default)
    {
        await _library.UpdateAsync(id, c =>
        {
            c.Status = status;
            c.Reason = reason == null ? null : Text.Clean(reason, 200);
        }, ct);
        events.Publish("capes.changed");
    }

    public async Task<byte[]> ReadAsync(string id, CancellationToken ct)
    {
        var item = await _library.GetAsync(id, ct) ?? throw new AxoException("errors.notFound");
        return await File.ReadAllBytesAsync(_library.FilePath(item), ct);
    }
}
