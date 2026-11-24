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

        public async Task<IReadOnlyList<TItem>> List(CancellationToken cancellationToken = default)
        {
            var document = await _store.Load(cancellationToken);
            return document.Items.Where(item => File.Exists(FilePath(item))).ToList();
        }

        public async Task<TItem?> Get(string id, CancellationToken cancellationToken = default) =>
            (await List(cancellationToken)).FirstOrDefault(libraryItem => libraryItem.Id == id);

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

        public async Task Add(TItem item, byte[] png, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Folder);
            await File.WriteAllBytesAsync(_pathGuard.Combine(Folder, item.File), png, cancellationToken);
            await _store.Update(document =>
            {
                document.Items.RemoveAll(libraryItem => libraryItem.Id == item.Id);
                document.Items.Insert(0, item);
            }, cancellationToken);
        }

        public Task Update(string id, Action<TItem> change, CancellationToken cancellationToken = default) =>
            _store.Update(document =>
            {
                var item = document.Items.FirstOrDefault(libraryItem => libraryItem.Id == id) ?? throw new AxoException("errors.notFound");
                change(item);
            }, cancellationToken);

        public async Task Remove(string id, CancellationToken cancellationToken = default)
        {
            TItem? removed = null;
            await _store.Update(document =>
            {
                removed = document.Items.FirstOrDefault(item => item.Id == id);
                document.Items.RemoveAll(item => item.Id == id);
            }, cancellationToken);
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
