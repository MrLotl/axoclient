using AxoClient.DataModel.Game;

namespace AxoClient.Game.Contract
{
    public interface IVersionManager
    {
        Task<IReadOnlyList<GameVersionInfo>> List(CancellationToken ct = default);

        Task<string> LatestRelease(CancellationToken ct = default);

        /// <summary>Adresse der Versionsdatei (JSON) bei Mojang, oder <c>null</c>, wenn es die Version nicht gibt.</summary>
        Task<string?> UrlOf(string version, CancellationToken ct = default);

        Task<VersionType?> TypeOf(string version, CancellationToken ct = default);
    }
}
