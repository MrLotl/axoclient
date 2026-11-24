using AxoClient.Common;
using AxoClient.DataModel.Skins;

namespace AxoClient.Skins
{
    /// <summary>Eine Bibliothek aus PNG-Dateien plus Index (Skins oder Capes). Dateien nur über IDs, nie über Pfade aus der UI.</summary>
    public sealed class LibraryStore<TDoc, TItem>
        where TDoc : class, ILibraryDocument<TItem>, new()
        where TItem : class, ILibraryItem
    {
        private readonly ITextHelper _textHelper;
        private readonly IPathGuard _pathGuard;
        private readonly IJsonStoreFactory _jsonStoreFactory;
        private readonly JsonStore<TDoc> _store;

        public LibraryStore(
            AppPaths paths,
            string folder,
            string indexPath,
            ITextHelper textHelper,
            IPathGuard pathGuard,
            IJsonStoreFactory jsonStoreFactory)
        {
            _textHelper = textHelper;
            _pathGuard = pathGuard;
            _jsonStoreFactory = jsonStoreFactory;
            _store = _jsonStoreFactory.Create<TDoc>(paths.Fs, indexPath);
            Folder = folder;
        }

        public string Folder { get; }

        public async Task<IReadOnlyList<TItem>> List(CancellationToken ct = default)
        {
            var doc = await _store.Load(ct);
            return doc.Items.Where(i => File.Exists(FilePath(i))).ToList();
        }

        public async Task<TItem?> Get(string id, CancellationToken ct = default) =>
            (await List(ct)).FirstOrDefault(i => i.Id == id);

        public string FilePath(TItem item) => _pathGuard.Combine(Folder, item.File);

        public string? FilePathById(string id)
        {
            if (!_textHelper.IsValidId(id))
            {
                return null;
            }

            var path = Path.Combine(Folder, id + ".png");
            return File.Exists(path) ? path : null;
        }

        public async Task Add(TItem item, byte[] png, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Folder);
            await File.WriteAllBytesAsync(_pathGuard.Combine(Folder, item.File), png, ct);
            await _store.Update(doc =>
            {
                doc.Items.RemoveAll(i => i.Id == item.Id);
                doc.Items.Insert(0, item);
            }, ct);
        }

        public Task Update(string id, Action<TItem> change, CancellationToken ct = default) =>
            _store.Update(doc =>
            {
                var item = doc.Items.FirstOrDefault(i => i.Id == id) ?? throw new AxoException("errors.notFound");
                change(item);
            }, ct);

        public async Task Remove(string id, CancellationToken ct = default)
        {
            TItem? removed = null;
            await _store.Update(doc =>
            {
                removed = doc.Items.FirstOrDefault(i => i.Id == id);
                doc.Items.RemoveAll(i => i.Id == id);
            }, ct);
            if (removed != null)
            {
                var path = _pathGuard.Combine(Folder, removed.File);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
