using AxoClient.Core.Accounts;
using AxoClient.Core.Common;

namespace AxoClient.Core.Skins;

public enum SkinOriginType
{
    Upload,
    Player,
    Mojang
}

public sealed class SkinOrigin
{
    public SkinOriginType Type { get; set; } = SkinOriginType.Upload;

    /// <summary>Bei "Von Spieler übernehmen": Name des Spielers.</summary>
    public string? Player { get; set; }
}

public sealed class SkinItem : ILibraryItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public SkinModel Model { get; set; } = SkinModel.Classic;
    public SkinOrigin Origin { get; set; } = new();
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SkinLibraryDocument : ILibraryDocument<SkinItem>
{
    public int FormatVersion { get; set; } = 1;
    public List<SkinItem> Skins { get; set; } = [];

    List<SkinItem> ILibraryDocument<SkinItem>.Items => Skins;
}

public sealed record SkinView(string Id, string Name, SkinModel Model, SkinOriginType Origin, string? Player, string Url, bool Active);

/// <summary>Skin-Bibliothek (nur lokal) und Wechsel des Skins bei Mojang.</summary>
public sealed class SkinManager(AppPaths paths, MojangApi mojang, AccountManager accounts, EventHub events)
{
    private readonly LibraryStore<SkinLibraryDocument, SkinItem> _library = new(paths, paths.Skins, paths.SkinLibrary);

    public async Task<IReadOnlyList<SkinView>> ListAsync(CancellationToken ct = default)
    {
        var active = accounts.Active?.Skin;
        return (await _library.ListAsync(ct))
            .Select(s => new SkinView(s.Id, s.Name, s.Model, s.Origin.Type, s.Origin.Player, $"/axo/skin-file/{s.Id}", s.Id == active))
            .ToList();
    }

    public string? FilePath(string id) => _library.FilePathById(id);

    public async Task<SkinView> AddAsync(byte[] png, string name, SkinModel model, SkinOrigin? origin = null, CancellationToken ct = default)
    {
        if (!SkinFiles.IsValidSkin(png))
            throw new AxoException("skins.invalidImage");
        var id = SkinFiles.IdOf(png);
        var item = new SkinItem
        {
            Id = id,
            Name = Text.Clean(name, 40) is { Length: > 0 } n ? n : "Skin",
            File = id + ".png",
            Model = model,
            Origin = origin ?? new SkinOrigin()
        };
        await _library.AddAsync(item, png, ct);
        events.Publish("skins.changed");
        return new SkinView(item.Id, item.Name, item.Model, item.Origin.Type, item.Origin.Player, $"/axo/skin-file/{item.Id}", false);
    }

    /// <summary>"Von Spieler übernehmen": Skin eines anderen Spielers in die Bibliothek legen.</summary>
    public async Task<SkinView> AdoptFromPlayerAsync(string playerName, CancellationToken ct)
    {
        var found = await mojang.LookUpAsync(playerName, ct) ?? throw new AxoException("skins.playerNotFound");
        var textures = await mojang.GetTexturesAsync(found.Uuid, ct);
        if (textures?.SkinUrl == null)
            throw new AxoException("skins.playerHasNoSkin");
        var png = await mojang.DownloadTextureAsync(textures.SkinUrl, ct);
        return await AddAsync(png, found.Name, textures.Model, new SkinOrigin { Type = SkinOriginType.Player, Player = found.Name }, ct);
    }

    public async Task UpdateAsync(string id, string? name, SkinModel? model, byte[]? newPng, CancellationToken ct)
    {
        if (newPng != null)
        {
            var old = await _library.GetAsync(id, ct) ?? throw new AxoException("errors.notFound");
            await _library.RemoveAsync(id, ct);
            await AddAsync(newPng, name ?? old.Name, model ?? old.Model, old.Origin, ct);
            return;
        }
        await _library.UpdateAsync(id, s =>
        {
            if (name != null)
                s.Name = Text.Clean(name, 40) is { Length: > 0 } n ? n : s.Name;
            if (model != null)
                s.Model = model.Value;
        }, ct);
        events.Publish("skins.changed");
    }

    public async Task RemoveAsync(string id, CancellationToken ct)
    {
        await _library.RemoveAsync(id, ct);
        events.Publish("skins.changed");
    }

    /// <summary>Skin bei Mojang hochladen und für den aktiven Account merken.</summary>
    public async Task<MojangProfile> ApplyAsync(string id, CancellationToken ct)
    {
        var item = await _library.GetAsync(id, ct) ?? throw new AxoException("errors.notFound");
        var png = await File.ReadAllBytesAsync(_library.FilePath(item), ct);
        var profile = await mojang.UploadSkinAsync(png, item.Model, ct);
        if (accounts.Active is { } account)
            await accounts.UpdateEntryAsync(account.Uuid, a => a.Skin = id);
        await CacheOwnSkinAsync(profile, ct);
        events.Publish("skins.changed");
        return profile;
    }

    /// <summary>Den aktuellen Mojang-Skin des Accounts in den Kopf-Cache legen.</summary>
    public async Task CacheOwnSkinAsync(MojangProfile profile, CancellationToken ct)
    {
        if (profile.SkinUrl == null)
            return;
        var png = await mojang.DownloadTextureAsync(profile.SkinUrl, ct);
        Directory.CreateDirectory(paths.Heads);
        await File.WriteAllBytesAsync(Path.Combine(paths.Heads, profile.Uuid.Replace("-", "") + ".png"), png, ct);
        events.Publish("accounts.changed");
    }

    /// <summary>Skin-PNG eines Spielers für Köpfe (Cache 1 Tag, sonst von Mojang).</summary>
    public async Task<byte[]?> PlayerSkinAsync(string uuid, CancellationToken ct)
    {
        var compact = uuid.Replace("-", "").ToLowerInvariant();
        if (compact.Length != 32 || !compact.All(Uri.IsHexDigit))
            return null;
        var cache = Path.Combine(paths.Heads, compact + ".png");
        if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(1))
            return await File.ReadAllBytesAsync(cache, ct);
        try
        {
            var textures = await mojang.GetTexturesAsync(compact, ct);
            if (textures?.SkinUrl == null)
                return File.Exists(cache) ? await File.ReadAllBytesAsync(cache, ct) : null;
            var png = await mojang.DownloadTextureAsync(textures.SkinUrl, ct);
            Directory.CreateDirectory(paths.Heads);
            await File.WriteAllBytesAsync(cache, png, ct);
            return png;
        }
        catch (Exception ex) when (ex is HttpRequestException or AxoException or IOException)
        {
            return File.Exists(cache) ? await File.ReadAllBytesAsync(cache, ct) : null;
        }
    }
}
