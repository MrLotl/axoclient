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
            if (parts.Length is < 2 or > 3 || !parts.All(p => p.All(char.IsAsciiDigit) && p.Length > 0))
            {
                return null;
            }
            return (int.Parse(parts[0]), int.Parse(parts[1]), parts.Length == 3 ? int.Parse(parts[2]) : 0);
        }

        public int Compare(string a, string b)
        {
            var pa = Parse(a);
            var pb = Parse(b);
            if (pa == null || pb == null)
            {
                return string.CompareOrdinal(a, b);
            }
            return pa.Value.CompareTo(pb.Value);
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
        public string NeoForgeVersionId(string mc, string loaderVersion)
        {
            if (mc == NeoForgeLoader.LegacyMinecraft)
            {
                return $"{mc}-forge-{loaderVersion}";
            }
            return $"neoforge-{loaderVersion}";
        }

        [GeneratedRegex(@"^[A-Za-z0-9._+\-]+$")]
        private partial Regex safeVersion();
    }
}
