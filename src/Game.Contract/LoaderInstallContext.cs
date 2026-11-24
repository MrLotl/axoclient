using CmlLib.Core;
using CmlLib.Core.Installers;

namespace AxoClient.Game.Contract
{
    /// <summary>Was ein Loader-Installer zum Arbeiten bekommt.</summary>
    public sealed record LoaderInstallContext(
        MinecraftLauncher Launcher,
        MinecraftPath Path,
        string MinecraftVersion,
        string? LoaderVersion,
        IProgress<string> Status,
        IProgress<InstallerProgressChangedEventArgs> Files,
        IProgress<ByteProgress> Bytes,
        CancellationToken Token);
}
