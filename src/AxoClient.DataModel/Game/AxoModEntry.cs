namespace AxoClient.DataModel.Game
{
    /// <summary>Eine Jar der AxoMod: für welche MC-Versionen und Loader sie gilt.</summary>
    public sealed class AxoModEntry
    {
        public List<string> Minecraft { get; set; } = [];
        public List<LoaderType> Loaders { get; set; } = [];
        public string File { get; set; } = "";

        /// <summary><c>full</c> oder <c>reduced</c> (1.8.9: ohne Axo-Menü).</summary>
        public string Level { get; set; } = "full";

        /// <summary>SHA-256 der Jar (klein, hex); schreibt <c>mod/build-all.ps1</c>.</summary>
        public string? Sha256 { get; set; }
    }
}
