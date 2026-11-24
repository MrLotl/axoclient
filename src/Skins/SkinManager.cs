using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Skins;
using AxoClient.Skins.Contract;

namespace AxoClient.Skins
{
    /// <summary>Skin-Bibliothek (nur lokal) und Wechsel des Skins bei Mojang.</summary>
    public sealed class SkinManager : ISkinManager
    {
        private readonly AppPaths _paths;
        private readonly IMojangApi _mojang;
        private readonly IAccountManager _accounts;
        private readonly IEventHub _events;
        private readonly ITextHelper _textHelper;
        private readonly ISkinFiles _skinFiles;
        private readonly IPathGuard _pathGuard;
        private readonly IJsonStoreFactory _jsonStoreFactory;
        private readonly LibraryStore<SkinLibraryDocument, SkinItem> _library;

        public SkinManager(
            AppPaths paths,
            IMojangApi mojang,
            IAccountManager accounts,
            IEventHub events,
            ITextHelper textHelper,
            ISkinFiles skinFiles,
            IPathGuard pathGuard,
            IJsonStoreFactory jsonStoreFactory)
        {
            _paths = paths;
            _mojang = mojang;
            _accounts = accounts;
            _events = events;
            _textHelper = textHelper;
            _skinFiles = skinFiles;
            _pathGuard = pathGuard;
            _jsonStoreFactory = jsonStoreFactory;
            _library = new(paths, paths.Skins, paths.SkinLibrary, _textHelper, _pathGuard, _jsonStoreFactory);
        }

        public async Task<IReadOnlyList<SkinView>> List(CancellationToken cancellationToken = default)
        {
            var active = _accounts.Active?.Skin;
            return (await _library.List(cancellationToken))
                .Select(skinItem => new SkinView(skinItem.Id, skinItem.Name, skinItem.Model, skinItem.Origin.Type, skinItem.Origin.Player, $"/axo/skin-file/{skinItem.Id}", skinItem.Id == active))
                .ToList();
        }

        public string? FilePath(string id) => _library.FilePathById(id);

        public async Task<SkinView> Add(byte[] png, string name, SkinModel model, SkinOrigin? origin = null, CancellationToken cancellationToken = default)
        {
            if (!_skinFiles.IsValidSkin(png))
            {
                throw new AxoException("skins.invalidImage");
            }

            var id = _skinFiles.IdOf(png);
            var item = new SkinItem
            {
                Id = id,
                Name = _textHelper.Clean(name, 40) is { Length: > 0 } cleanName ? cleanName : "Skin",
                File = id + ".png",
                Model = model,
                Origin = origin ?? new SkinOrigin()
            };
            await _library.Add(item, png, cancellationToken);
            _events.Publish("skins.changed");
            return new SkinView(item.Id, item.Name, item.Model, item.Origin.Type, item.Origin.Player, $"/axo/skin-file/{item.Id}", false);
        }

        /// <summary>"Von Spieler übernehmen": Skin eines anderen Spielers in die Bibliothek legen.</summary>
        public async Task<SkinView> AdoptFromPlayer(string playerName, CancellationToken cancellationToken)
        {
            var found = await _mojang.LookUp(playerName, cancellationToken) ?? throw new AxoException("skins.playerNotFound");
            var textures = await _mojang.GetTextures(found.Uuid, cancellationToken);
            if (textures?.SkinUrl == null)
            {
                throw new AxoException("skins.playerHasNoSkin");
            }

            var png = await _mojang.DownloadTexture(textures.SkinUrl, cancellationToken);
            return await Add(png, found.Name, textures.Model, new SkinOrigin { Type = SkinOriginType.Player, Player = found.Name }, cancellationToken);
        }

        public async Task Update(string id, string? name, SkinModel? model, byte[]? newPng, CancellationToken cancellationToken)
        {
            if (newPng != null)
            {
                var old = await _library.Get(id, cancellationToken) ?? throw new AxoException("errors.notFound");
                await _library.Remove(id, cancellationToken);
                await Add(newPng, name ?? old.Name, model ?? old.Model, old.Origin, cancellationToken);
                return;
            }
            await _library.Update(id, skinItem =>
            {
                if (name != null)
                {
                    skinItem.Name = _textHelper.Clean(name, 40) is { Length: > 0 } cleanName ? cleanName : skinItem.Name;
                }

                if (model != null)
                {
                    skinItem.Model = model.Value;
                }
            }, cancellationToken);
            _events.Publish("skins.changed");
        }

        public async Task Remove(string id, CancellationToken cancellationToken)
        {
            await _library.Remove(id, cancellationToken);
            _events.Publish("skins.changed");
        }

        /// <summary>Skin bei Mojang hochladen und für den aktiven Account merken.</summary>
        public async Task<MojangProfile> Apply(string id, CancellationToken cancellationToken)
        {
            var item = await _library.Get(id, cancellationToken) ?? throw new AxoException("errors.notFound");
            var png = await File.ReadAllBytesAsync(_library.FilePath(item), cancellationToken);
            var profile = await _mojang.UploadSkin(png, item.Model, cancellationToken);
            if (_accounts.Active is { } account)
            {
                await _accounts.UpdateEntry(account.Uuid, accountEntry => accountEntry.Skin = id);
            }

            await CacheOwnSkin(profile, cancellationToken);
            _events.Publish("skins.changed");
            return profile;
        }

        /// <summary>Den aktuellen Mojang-Skin des Accounts in den Kopf-Cache legen.</summary>
        public async Task CacheOwnSkin(MojangProfile profile, CancellationToken cancellationToken)
        {
            if (profile.SkinUrl == null)
            {
                return;
            }

            var png = await _mojang.DownloadTexture(profile.SkinUrl, cancellationToken);
            Directory.CreateDirectory(_paths.Heads);
            await File.WriteAllBytesAsync(Path.Combine(_paths.Heads, profile.Uuid.Replace("-", "") + ".png"), png, cancellationToken);
            _events.Publish("accounts.changed");
        }

        /// <summary>Skin-PNG eines Spielers für Köpfe (Cache 1 Tag, sonst von Mojang).</summary>
        public async Task<byte[]?> PlayerSkin(string uuid, CancellationToken cancellationToken)
        {
            var compact = uuid.Replace("-", "").ToLowerInvariant();
            if (compact.Length != 32 || !compact.All(Uri.IsHexDigit))
            {
                return null;
            }

            var cache = Path.Combine(_paths.Heads, compact + ".png");
            if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(1))
            {
                return await File.ReadAllBytesAsync(cache, cancellationToken);
            }

            try
            {
                var textures = await _mojang.GetTextures(compact, cancellationToken);
                if (textures?.SkinUrl == null)
                {
                    return File.Exists(cache) ? await File.ReadAllBytesAsync(cache, cancellationToken) : null;
                }

                var png = await _mojang.DownloadTexture(textures.SkinUrl, cancellationToken);
                Directory.CreateDirectory(_paths.Heads);
                await File.WriteAllBytesAsync(cache, png, cancellationToken);
                return png;
            }
            catch (Exception exception) when (exception is HttpRequestException or AxoException or IOException)
            {
                return File.Exists(cache) ? await File.ReadAllBytesAsync(cache, cancellationToken) : null;
            }
        }
    }
}
