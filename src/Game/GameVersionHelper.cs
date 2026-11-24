using System.Text.RegularExpressions;
using AxoClient.Game.Contract;

namespace AxoClient.Game
{
    /// <summary>Prüft und vergleicht Minecraft- und Loader-Versionen.</summary>
    public partial class GameVersionHelper : IGameVersionHelper
    {
        /// <summary>Nur harmlose Zeichen: Versionen landen in Pfaden und Argumenten.</summary>
        public bool IsSafeVersion(string? version)
        {
            return version is { Length: > 0 and <= 60 } && safeVersion().IsMatch(version);
        }

        /// <summary>"1.21.4" → (1, 21, 4); Snapshots und alles andere → null.</summary>
        public (int Major, int Minor, int Patch)? Parse(string version)
        {
            var parts = version.Split('.');
            if (parts.Length is < 2 or > 3 || !parts.All(part => part.All(char.IsAsciiDigit) && part.Length > 0))
            {
                return null;
            }
            return (int.Parse(parts[0]), int.Parse(parts[1]), parts.Length == 3 ? int.Parse(parts[2]) : 0);
        }

        public int Compare(string firstVersion, string secondVersion)
        {
            var firstParsed = Parse(firstVersion);
            var secondParsed = Parse(secondVersion);
            if (firstParsed == null || secondParsed == null)
            {
                return string.CompareOrdinal(firstVersion, secondVersion);
            }
            return firstParsed.Value.CompareTo(secondParsed.Value);
        }

        /// <summary>Java-Hauptversion, die eine Minecraft-Version braucht (unbekannt: 21).</summary>
        public int RequiredJava(string mcVersion)
        {
            var parsed = Parse(mcVersion);
            if (parsed == null)
            {
                return 21;
            }
            var (major, minor, patch) = parsed.Value;
            if (major >= 26)
            {
                return 25;
            }
            if (minor > 20 || (minor == 20 && patch >= 5))
            {
                return 21;
            }
            if (minor >= 18)
            {
                return 17;
            }
            return minor == 17 ? 16 : 8;
        }

        /// <summary>Versions-ID im versions-Ordner: 1.20.1 heißt wie Forge („1.20.1-forge-47.1.106“), danach „neoforge-21.4.x“.</summary>
        public string NeoForgeVersionId(string minecraftVersion, string loaderVersion)
        {
            if (minecraftVersion == NeoForgeLoader.LegacyMinecraft)
            {
                return $"{minecraftVersion}-forge-{loaderVersion}";
            }
            return $"neoforge-{loaderVersion}";
        }

        [GeneratedRegex(@"^[A-Za-z0-9._+\-]+$")]
        private partial Regex safeVersion();
    }
}
