using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;

namespace AxoClient.Game
{
    public class LoaderNames : ILoaderNames
    {
        public string Display(LoaderType type) => type switch
        {
            LoaderType.Vanilla => "Vanilla",
            LoaderType.Fabric => "Fabric",
            LoaderType.Forge => "Forge",
            LoaderType.NeoForge => "NeoForge",
            LoaderType.Quilt => "Quilt",
            _ => type.ToString()
        };

        /// <summary>Bezeichnung wie bei Modrinth (<c>loaders</c>-Filter).</summary>
        public string ModrinthId(LoaderType type) => type switch
        {
            LoaderType.NeoForge => "neoforge",
            _ => type.ToString().ToLowerInvariant()
        };

        public bool SupportsMods(LoaderType type) => type != LoaderType.Vanilla;
    }
}
