using AxoClient.DataModel.Game;

namespace AxoClient.Game.Contract
{
    public interface IVersionManager
    {
        Task<IReadOnlyList<GameVersionInfo>> List(CancellationToken cancellationToken = default);

        Task<string> LatestRelease(CancellationToken cancellationToken = default);

        /// <summary>Adresse der Versionsdatei (JSON) bei Mojang, oder <c>null</c>, wenn es die Version nicht gibt.</summary>
        Task<string?> UrlOf(string version, CancellationToken cancellationToken = default);

        Task<VersionType?> TypeOf(string version, CancellationToken cancellationToken = default);
    }
}
