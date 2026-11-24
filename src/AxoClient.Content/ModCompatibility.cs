using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.Launch.Contract;

namespace AxoClient.Content
{
    /// <summary>Passt eine Mod zu Loader und Minecraft-Version einer Instanz?</summary>
    public class ModCompatibility : IModCompatibility
    {
        private readonly IVersionRange _versionRange;

        public ModCompatibility(IVersionRange versionRange)
        {
            _versionRange = versionRange;
        }

        /// <summary>
        /// Passt eine Mod zum Loader der Instanz? NeoForge bis 1.20.4 nutzt noch Forges mods.toml (und lädt unter 1.20.1
        /// Forge-Mods direkt), solche Jars gelten dort deshalb als passend.
        /// </summary>
        public bool LoaderFits(List<string> modLoaders, LoaderType loader, string mc)
        {
            return loader switch
            {
                LoaderType.Quilt => modLoaders.Any(l => l is "quilt" or "fabric"),
                LoaderType.Fabric => modLoaders.Contains("fabric"),
                LoaderType.Forge => modLoaders.Contains("forge"),
                LoaderType.NeoForge => modLoaders.Contains("neoforge")
                                       || (modLoaders.Contains("forge") && _versionRange.Satisfies(mc, "<1.20.5", unknown: false)),
                _ => true
            };
        }

        /// <summary>Passt eine lokale Mod laut ihren Metadaten (Loader und Minecraft-Version)?</summary>
        public bool ModFits(ModMetadata meta, LoaderType loader, string mc)
        {
            return (meta.Loaders.Count == 0 || LoaderFits(meta.Loaders, loader, mc))
                   && (meta.MinecraftRange is not { } range || _versionRange.SatisfiesLoosely(mc, range));
        }
    }
}
