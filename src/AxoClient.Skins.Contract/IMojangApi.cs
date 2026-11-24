using AxoClient.DataModel.Skins;

namespace AxoClient.Skins.Contract
{
    public interface IMojangApi
    {
        Task<MojangProfile> GetOwnProfile(CancellationToken ct);

        Task<MojangProfile> UploadSkin(byte[] png, SkinModel model, CancellationToken ct);

        /// <summary>Mojang-Cape setzen oder (mit <c>null</c>) ablegen.</summary>
        Task<MojangProfile> SetCape(string? capeId, CancellationToken ct);

        /// <summary>Skin und Cape eines beliebigen Spielers (für "Von Spieler übernehmen" und Köpfe).</summary>
        Task<PlayerTextures?> GetTextures(string uuid, CancellationToken ct);

        Task<(string Uuid, string Name)?> LookUp(string name, CancellationToken ct);

        /// <summary>Lädt eine Textur von Mojang (nur textures.minecraft.net).</summary>
        Task<byte[]> DownloadTexture(string url, CancellationToken ct);
    }
}
