using AxoClient.DataModel.Skins;

namespace AxoClient.Skins.Contract
{
    public interface ISkinManager
    {
        Task<IReadOnlyList<SkinView>> List(CancellationToken ct = default);

        string? FilePath(string id);

        Task<SkinView> Add(byte[] png, string name, SkinModel model, SkinOrigin? origin = null, CancellationToken ct = default);

        /// <summary>"Von Spieler übernehmen": Skin eines anderen Spielers in die Bibliothek legen.</summary>
        Task<SkinView> AdoptFromPlayer(string playerName, CancellationToken ct);

        Task Update(string id, string? name, SkinModel? model, byte[]? newPng, CancellationToken ct);

        Task Remove(string id, CancellationToken ct);

        /// <summary>Skin bei Mojang hochladen und für den aktiven Account merken.</summary>
        Task<MojangProfile> Apply(string id, CancellationToken ct);

        /// <summary>Den aktuellen Mojang-Skin des Accounts in den Kopf-Cache legen.</summary>
        Task CacheOwnSkin(MojangProfile profile, CancellationToken ct);

        /// <summary>Skin-PNG eines Spielers für Köpfe (Cache 1 Tag, sonst von Mojang).</summary>
        Task<byte[]?> PlayerSkin(string uuid, CancellationToken ct);
    }
}
