using AxoClient.DataModel.Game;

namespace AxoClient.Game.Contract
{
    public interface ILoaderManager
    {
        ILoaderInstaller? For(LoaderType type);

        Task<IReadOnlyList<LoaderVersionInfo>> ListVersions(LoaderType type, string minecraftVersion, CancellationToken cancellationToken);

        /// <summary>Gewählte oder empfohlene Loader-Version.</summary>
        Task<string?> ResolveVersion(LoaderType type, string minecraftVersion, string? wanted, CancellationToken cancellationToken);
    }
}
