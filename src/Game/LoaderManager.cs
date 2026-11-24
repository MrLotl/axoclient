using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;

namespace AxoClient.Game
{
    public sealed partial class LoaderManager : ILoaderManager
    {
        private readonly ILoaderNames _loaderNames;
        private readonly IGameVersionHelper _gameVersionHelper;
        private readonly Dictionary<LoaderType, ILoaderInstaller> _installers;

        public LoaderManager(
            IEnumerable<ILoaderInstaller> installers,
            ILoaderNames loaderNames,
            IGameVersionHelper gameVersionHelper)
        {
            _loaderNames = loaderNames;
            _gameVersionHelper = gameVersionHelper;
            _installers = installers.ToDictionary(installer => installer.Type);
        }

        public ILoaderInstaller? For(LoaderType type) => _installers.GetValueOrDefault(type);

        public Task<IReadOnlyList<LoaderVersionInfo>> ListVersions(LoaderType type, string minecraftVersion, CancellationToken cancellationToken) =>
            For(type)?.ListVersions(minecraftVersion, cancellationToken) ?? Task.FromResult<IReadOnlyList<LoaderVersionInfo>>([]);

        /// <summary>Gewählte oder empfohlene Loader-Version.</summary>
        public async Task<string?> ResolveVersion(LoaderType type, string minecraftVersion, string? wanted, CancellationToken cancellationToken)
        {
            if (type == LoaderType.Vanilla)
            {
                return null;
            }

            if (_gameVersionHelper.IsSafeVersion(wanted))
            {
                return wanted;
            }

            var versions = await ListVersions(type, minecraftVersion, cancellationToken);
            return (versions.FirstOrDefault(version => version.Recommended) ?? versions.FirstOrDefault(version => version.Stable) ?? versions.FirstOrDefault())?.Version
                   ?? throw new AxoException("game.loaderUnsupported",
                       new() { ["loader"] = _loaderNames.Display(type), ["version"] = minecraftVersion });
        }
    }
}
