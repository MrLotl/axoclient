using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;

namespace AxoClient.Game.Contract
{
    public interface IAxoModInstaller
    {
        AxoModEntry? Find(string minecraftVersion, LoaderType loader);

        bool IsSupported(string minecraftVersion, LoaderType loader);

        IReadOnlyList<string> SupportedMinecraftVersions { get; }

        Task<byte[]> ReadJar(AxoModEntry entry, CancellationToken ct);

        /// <summary>
        /// Ob die Jar unter <paramref name="path"/> genau die mitgelieferte ist (SHA-256). Verglichen wird der Inhalt, nicht
        /// die Launcher-Version: ein neuer Mod-Build ohne neue Launcher-Version (z. B. 1.0.0-dev) wird so trotzdem ersetzt.
        /// </summary>
        bool IsCurrent(AxoModEntry entry, string path);

        /// <summary>Legt die richtige Jar in <c>mods/</c> und entfernt alte AxoMod-Jars.</summary>
        Task<ContentEntry?> Sync(string modsDir, string minecraftVersion, LoaderType loader, bool enabled,
        CancellationToken ct);

        bool IsAxoModFile(string fileName);
    }
}
