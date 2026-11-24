using AxoClient.DataModel.Game;

namespace AxoClient.Game.Contract
{
    public interface ILoaderManager
    {
        ILoaderInstaller? For(LoaderType type);

        Task<IReadOnlyList<LoaderVersionInfo>> ListVersions(LoaderType type, string mc, CancellationToken ct);

        /// <summary>Gewählte oder empfohlene Loader-Version.</summary>
        Task<string?> ResolveVersion(LoaderType type, string mc, string? wanted, CancellationToken ct);
    }
}
