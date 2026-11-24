using System.Security.Cryptography;
using System.Text;
using AxoClient.Common;
using AxoClient.DataModel.Game;

namespace AxoClient.DataModel.Import
{
    /// <summary>Eine Instanz (oder ein Profil) in einem anderen Launcher. Pfade bleiben in C#, die UI kennt nur die Id.</summary>
    public sealed class ForeignInstance
    {
        public required string Launcher { get; init; }
        public required string Name { get; init; }
        public required string GameDir { get; init; }

        /// <summary>Spielordner wird mit anderen Profilen geteilt (z. B. .minecraft): nur ausgewählte Inhalte kopieren.</summary>
        public bool Shared { get; init; }

        /// <summary>Mods liegen im üblichen <c>mods</c>-Ordner des Spielordners.</summary>
        public bool GameDirMods { get; init; } = true;

        public string? ExtraModsDir { get; init; }
        public string? ExtraConfigDir { get; init; }
        public string? ExtraDataDir { get; init; }
        public List<ForeignModrinthMod> ModrinthMods { get; init; } = [];

        public LoaderType? Loader { get; set; }
        public string? MinecraftVersion { get; set; }
        public string? LoaderVersion { get; set; }

        public int Worlds { get; set; }
        public int Mods { get; set; }
        public TextRef? Hint { get; set; }
        public TextRef? Problem { get; set; }

        public string? ModsDir => ExtraModsDir ?? (GameDirMods ? Path.Combine(GameDir, "mods") : null);

        public string Id => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Launcher}|{GameDir}|{ExtraModsDir}|{Name}")))
            .ToLowerInvariant()[..16];

        public ForeignInstanceView ToView() =>
            new(Id, Name, Loader, MinecraftVersion, LoaderVersion, Worlds, Mods, Shared, ModrinthMods.Count, Hint, Problem);
    }
}
