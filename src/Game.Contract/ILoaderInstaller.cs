using AxoClient.DataModel.Game;
using CmlLib.Core;

namespace AxoClient.Game.Contract
{
    /// <summary>Ein Mod-Loader (Fabric, Forge, NeoForge, Quilt).</summary>
    public interface ILoaderInstaller
    {
        LoaderType Type { get; }

        Task<IReadOnlyList<LoaderVersionInfo>> ListVersions(string minecraftVersion, CancellationToken cancellationToken);

        /// <summary>Installiert den Loader und gibt die Versions-ID für CmlLib zurück.</summary>
        Task<string> Install(LoaderInstallContext context);

        /// <summary>Ist genau diese Loader-Version bereits installiert? (Für die Start-Pipeline.)</summary>
        bool IsInstalled(MinecraftPath path, string minecraftVersion, string? loaderVersion);
    }
}
