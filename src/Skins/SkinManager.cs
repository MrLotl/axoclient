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

        public async Task<IReadOnlyList<SkinView>> List(CancellationToken ct = default)
        {
            var active = _accounts.Active?.Skin;
            return (await _library.List(ct))
                .Select(s => new SkinView(s.Id, s.Name, s.Model, s.Origin.Type, s.Origin.Player, $"/axo/skin-file/{s.Id}", s.Id == active))
                .ToList();
        }

        public string? FilePath(string id) => _library.FilePathById(id);

        public async Task<SkinView> Add(byte[] png, string name, SkinModel model, SkinOrigin? origin = null, CancellationToken ct = default)
        {
            if (!_skinFiles.IsValidSkin(png))
            {
                throw new AxoException("skins.invalidImage");
            }

            var id = _skinFiles.IdOf(png);
            var item = new SkinItem
            {
                Id = id,
                Name = _textHelper.Clean(name, 40) is { Length: > 0 } n ? n : "Skin",
                File = id + ".png",
                Model = model,
                Origin = origin ?? new SkinOrigin()
            };
            await _library.Add(item, png, ct);
            _events.Publish("skins.changed");
            return new SkinView(item.Id, item.Name, item.Model, item.Origin.Type, item.Origin.Player, $"/axo/skin-file/{item.Id}", false);
        }

        /// <summary>"Von Spieler übernehmen": Skin eines anderen Spielers in die Bibliothek legen.</summary>
        public async Task<SkinView> AdoptFromPlayer(string playerName, CancellationToken ct)
        {
            var found = await _mojang.LookUp(playerName, ct) ?? throw new AxoException("skins.playerNotFound");
            var textures = await _mojang.GetTextures(found.Uuid, ct);
            if (textures?.SkinUrl == null)
            {
                throw new AxoException("skins.playerHasNoSkin");
            }

            var png = await _mojang.DownloadTexture(textures.SkinUrl, ct);
            return await Add(png, found.Name, textures.Model, new SkinOrigin { Type = SkinOriginType.Player, Player = found.Name }, ct);
        }

        public async Task Update(string id, string? name, SkinModel? model, byte[]? newPng, CancellationToken ct)
        {
            if (newPng != null)
            {
                var old = await _library.Get(id, ct) ?? throw new AxoException("errors.notFound");
                await _library.Remove(id, ct);
                await Add(newPng, name ?? old.Name, model ?? old.Model, old.Origin, ct);
                return;
            }
            await _library.Update(id, s =>
            {
                if (name != null)
                {
                    s.Name = _textHelper.Clean(name, 40) is { Length: > 0 } n ? n : s.Name;
                }

                if (model != null)
                {
                    s.Model = model.Value;
                }
            }, ct);
            _events.Publish("skins.changed");
        }

        public async Task Remove(string id, CancellationToken ct)
        {
            await _library.Remove(id, ct);
            _events.Publish("skins.changed");
        }

        /// <summary>Skin bei Mojang hochladen und für den aktiven Account merken.</summary>
        public async Task<MojangProfile> Apply(string id, CancellationToken ct)
        {
            var item = await _library.Get(id, ct) ?? throw new AxoException("errors.notFound");
            var png = await File.ReadAllBytesAsync(_library.FilePath(item), ct);
            var profile = await _mojang.UploadSkin(png, item.Model, ct);
            if (_accounts.Active is { } account)
            {
                await _accounts.UpdateEntry(account.Uuid, a => a.Skin = id);
            }

            await CacheOwnSkin(profile, ct);
            _events.Publish("skins.changed");
            return profile;
        }

        /// <summary>Den aktuellen Mojang-Skin des Accounts in den Kopf-Cache legen.</summary>
        public async Task CacheOwnSkin(MojangProfile profile, CancellationToken ct)
        {
            if (profile.SkinUrl == null)
            {
                return;
            }

            var png = await _mojang.DownloadTexture(profile.SkinUrl, ct);
            Directory.CreateDirectory(_paths.Heads);
            await File.WriteAllBytesAsync(Path.Combine(_paths.Heads, profile.Uuid.Replace("-", "") + ".png"), png, ct);
            _events.Publish("accounts.changed");
        }

        /// <summary>Skin-PNG eines Spielers für Köpfe (Cache 1 Tag, sonst von Mojang).</summary>
        public async Task<byte[]?> PlayerSkin(string uuid, CancellationToken ct)
        {
            var compact = uuid.Replace("-", "").ToLowerInvariant();
            if (compact.Length != 32 || !compact.All(Uri.IsHexDigit))
            {
                return null;
            }

            var cache = Path.Combine(_paths.Heads, compact + ".png");
            if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(1))
            {
                return await File.ReadAllBytesAsync(cache, ct);
            }

            try
            {
                var textures = await _mojang.GetTextures(compact, ct);
                if (textures?.SkinUrl == null)
                {
                    return File.Exists(cache) ? await File.ReadAllBytesAsync(cache, ct) : null;
                }

                var png = await _mojang.DownloadTexture(textures.SkinUrl, ct);
                Directory.CreateDirectory(_paths.Heads);
                await File.WriteAllBytesAsync(cache, png, ct);
                return png;
            }
            catch (Exception ex) when (ex is HttpRequestException or AxoException or IOException)
            {
                return File.Exists(cache) ? await File.ReadAllBytesAsync(cache, ct) : null;
            }
        }
    }
}
