using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;
using AxoClient.DataModel.Skins;
using AxoClient.Instances.Contract;
using AxoClient.Skins.Contract;

namespace AxoClient.Skins
{
    /// <summary>Eigene Capes: lokal erstellen (Editor oder Datei), optional zur Veröffentlichung einreichen.</summary>
    public sealed class CapeManager : ICapeManager
    {
        private readonly AppPaths _paths;
        private readonly IAccountManager _accounts;
        private readonly IEventHub _events;
        private readonly ITextHelper _textHelper;
        private readonly IImageFormats _imageFormats;
        private readonly ISkinFiles _skinFiles;
        private readonly IJsonStoreFactory _jsonStoreFactory;
        private readonly IPathGuard _pathGuard;
        private readonly LibraryStore<CapeLibraryDocument, CapeItem> _library;

        public CapeManager(
            AppPaths paths,
            IAccountManager accounts,
            IEventHub events,
            ITextHelper textHelper,
            IImageFormats imageFormats,
            ISkinFiles skinFiles,
            IJsonStoreFactory jsonStoreFactory,
            IPathGuard pathGuard)
        {
            _paths = paths;
            _accounts = accounts;
            _events = events;
            _textHelper = textHelper;
            _imageFormats = imageFormats;
            _skinFiles = skinFiles;
            _jsonStoreFactory = jsonStoreFactory;
            _pathGuard = pathGuard;
            _library = new(paths, paths.Capes, paths.CapeLibrary, _textHelper, _pathGuard, _jsonStoreFactory);
        }

        public async Task<IReadOnlyList<CapeView>> List(CancellationToken cancellationToken = default)
        {
            var active = _accounts.Active?.Capes.Axo;
            return (await _library.List(cancellationToken)).Select(capeItem => new CapeView(capeItem.Id, capeItem.Name, capeItem.Resolution, capeItem.Source, capeItem.Status, capeItem.Reason,
                    $"/axo/cape-file/{capeItem.Id}", active is { Source: AxoCapeSource.Custom } && active.Id == capeItem.Id,
                    File.Exists(editPath(capeItem.Id))))
                .ToList();
        }

        public string? FilePath(string id) => _library.FilePathById(id);

        private string editPath(string id) => Path.Combine(_paths.Capes, _textHelper.RequireId(id) + ".edit.json");

        public async Task<CapeView> Save(string? id, string name, byte[] png, CapeSource source, CapeEditState? edit, CancellationToken cancellationToken)
        {
            if (!_skinFiles.IsValidCape(png))
            {
                throw new AxoException("capes.invalidImage");
            }

            var size = _imageFormats.PngSize(png)!.Value;
            var scale = size.Width / 64;
            var newId = id is { } existing && _textHelper.IsValidId(existing) ? existing : _textHelper.NewId(name);
            var old = await _library.Get(newId, cancellationToken);
            var item = new CapeItem
            {
                Id = newId,
                Name = _textHelper.Clean(name, 40) is { Length: > 0 } cleanName ? cleanName : "Cape",
                File = newId + ".png",
                Resolution = $"{10 * scale}x{16 * scale}",
                Source = source,
                // Geändert = wieder nur lokal, eine alte Freigabe gilt nicht für das neue Bild
                Status = CapeStatus.Local,
                CreatedAt = old?.CreatedAt ?? DateTimeOffset.UtcNow
            };
            await _library.Add(item, png, cancellationToken);
            if (edit != null)
            {
                await _jsonStoreFactory.Create<CapeEditState>(_paths.Fs, editPath(newId)).Save(edit, cancellationToken);
            }

            _events.Publish("capes.changed");
            return (await List(cancellationToken)).First(capeView => capeView.Id == newId);
        }

        public Task<CapeEditState> LoadEditState(string id, CancellationToken cancellationToken) =>
            _jsonStoreFactory.Create<CapeEditState>(_paths.Fs, editPath(id)).Load(cancellationToken);

        public async Task Remove(string id, CancellationToken cancellationToken)
        {
            await _library.Remove(id, cancellationToken);
            if (File.Exists(editPath(id)))
            {
                File.Delete(editPath(id));
            }

            _events.Publish("capes.changed");
        }

        public async Task SetStatus(string id, CapeStatus status, string? reason, CancellationToken cancellationToken = default)
        {
            await _library.Update(id, capeItem =>
            {
                capeItem.Status = status;
                capeItem.Reason = reason == null ? null : _textHelper.Clean(reason, 200);
            }, cancellationToken);
            _events.Publish("capes.changed");
        }

        public async Task<byte[]> Read(string id, CancellationToken cancellationToken)
        {
            var item = await _library.Get(id, cancellationToken) ?? throw new AxoException("errors.notFound");
            return await File.ReadAllBytesAsync(_library.FilePath(item), cancellationToken);
        }
    }
}
