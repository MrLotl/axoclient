using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;

namespace AxoClient.Content.Contract
{
    public interface IModCompatibility
    {
        /// <summary>Passt eine Mod zum Loader der Instanz?</summary>
        bool LoaderFits(List<string> modLoaders, LoaderType loader, string mc);

        /// <summary>Passt eine lokale Mod laut ihren Metadaten (Loader und Minecraft-Version)?</summary>
        bool ModFits(ModMetadata meta, LoaderType loader, string mc);
    }
}
