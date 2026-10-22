using AxoClient.Core.Common;

namespace AxoClient.Core.Skins;

/// <summary>Gemeinsame Form von <c>skins/library.json</c> und <c>capes/library.json</c>.</summary>
public interface ILibraryDocument<TItem> : IVersionedDocument
{
    List<TItem> Items { get; }
}

public interface ILibraryItem
{
    string Id { get; }
    string File { get; }
}

/// <summary>Eine Bibliothek aus PNG-Dateien plus Index (Skins oder Capes). Dateien nur über IDs, nie über Pfade aus der UI.</summary>
public sealed class LibraryStore<TDoc, TItem>(AppPaths paths, string folder, string indexPath)
    where TDoc : class, ILibraryDocument<TItem>, new()
    where TItem : class, ILibraryItem
{
    private readonly JsonStore<TDoc> _store = new(paths.Fs, indexPath);

    public string Folder { get; } = folder;

    public async Task<IReadOnlyList<TItem>> ListAsync(CancellationToken ct = default)
    {
        var doc = await _store.LoadAsync(ct);
        return doc.Items.Where(i => File.Exists(FilePath(i))).ToList();
    }

    public async Task<TItem?> GetAsync(string id, CancellationToken ct = default) =>
        (await ListAsync(ct)).FirstOrDefault(i => i.Id == id);

    public string FilePath(TItem item) => PathGuard.Combine(Folder, item.File);

    public string? FilePathById(string id)
    {
        if (!Text.IsValidId(id))
            return null;
        var path = Path.Combine(Folder, id + ".png");
        return File.Exists(path) ? path : null;
    }

    public async Task AddAsync(TItem item, byte[] png, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(PathGuard.Combine(Folder, item.File), png, ct);
        await _store.UpdateAsync(doc =>
        {
            doc.Items.RemoveAll(i => i.Id == item.Id);
            doc.Items.Insert(0, item);
        }, ct);
    }

    public Task UpdateAsync(string id, Action<TItem> change, CancellationToken ct = default) =>
        _store.UpdateAsync(doc =>
        {
            var item = doc.Items.FirstOrDefault(i => i.Id == id) ?? throw new AxoException("errors.notFound");
            change(item);
        }, ct);

    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        TItem? removed = null;
        await _store.UpdateAsync(doc =>
        {
            removed = doc.Items.FirstOrDefault(i => i.Id == id);
            doc.Items.RemoveAll(i => i.Id == id);
        }, ct);
        if (removed != null)
        {
            var path = PathGuard.Combine(Folder, removed.File);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
