namespace AxoClient.Game.Contract
{
    public interface IGameVersionHelper
    {
        /// <summary>Nur harmlose Zeichen: Versionen landen in Pfaden und Argumenten.</summary>
        bool IsSafeVersion(string? version);

        /// <summary>"1.21.4" → (1, 21, 4); Snapshots und alles andere → null.</summary>
        (int Major, int Minor, int Patch)? Parse(string version);

        int Compare(string a, string b);

        /// <summary>Java-Hauptversion, die eine Minecraft-Version braucht (unbekannt: 21).</summary>
        int RequiredJava(string mcVersion);

        /// <summary>Versions-ID im versions-Ordner: 1.20.1 heißt wie Forge („1.20.1-forge-47.1.106“), danach „neoforge-21.4.x“.</summary>
        string NeoForgeVersionId(string mc, string loaderVersion);
    }
}
