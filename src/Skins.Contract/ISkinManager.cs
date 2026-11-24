using AxoClient.DataModel.Skins;

namespace AxoClient.Skins.Contract
{
    public interface ISkinManager
    {
        Task<IReadOnlyList<SkinView>> List(CancellationToken cancellationToken = default);

        string? FilePath(string id);

        Task<SkinView> Add(byte[] png, string name, SkinModel model, SkinOrigin? origin = null, CancellationToken cancellationToken = default);

        /// <summary>"Von Spieler übernehmen": Skin eines anderen Spielers in die Bibliothek legen.</summary>
        Task<SkinView> AdoptFromPlayer(string playerName, CancellationToken cancellationToken);

        Task Update(string id, string? name, SkinModel? model, byte[]? newPng, CancellationToken cancellationToken);

        Task Remove(string id, CancellationToken cancellationToken);

        /// <summary>Skin bei Mojang hochladen und für den aktiven Account merken.</summary>
        Task<MojangProfile> Apply(string id, CancellationToken cancellationToken);

        /// <summary>Den aktuellen Mojang-Skin des Accounts in den Kopf-Cache legen.</summary>
        Task CacheOwnSkin(MojangProfile profile, CancellationToken cancellationToken);

        /// <summary>Skin-PNG eines Spielers für Köpfe (Cache 1 Tag, sonst von Mojang).</summary>
        Task<byte[]?> PlayerSkin(string uuid, CancellationToken cancellationToken);
    }
}
