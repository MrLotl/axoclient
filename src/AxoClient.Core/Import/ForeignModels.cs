using System.Text;
using AxoClient.Core.Common;
using AxoClient.Core.Game;

namespace AxoClient.Core.Import;

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

    public string Id => Text.Sha256Hex(Encoding.UTF8.GetBytes($"{Launcher}|{GameDir}|{ExtraModsDir}|{Name}"))[..16];

    public static bool IsModFile(string file) =>
        file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);

    public ForeignInstanceView ToView() =>
        new(Id, Name, Loader, MinecraftVersion, LoaderVersion, Worlds, Mods, Shared, ModrinthMods.Count, Hint, Problem);
}

public sealed record ForeignModrinthMod(string ProjectId, string? VersionId, bool Enabled);

public sealed record ForeignInstanceView(
    string Id,
    string Name,
    LoaderType? Loader,
    string? MinecraftVersion,
    string? LoaderVersion,
    int Worlds,
    int Mods,
    bool Shared,
    int ModrinthMods,
    TextRef? Hint,
    TextRef? Problem);

public sealed record ForeignLauncherView(string Id, string Name, bool Installed, IReadOnlyList<ForeignInstanceView> Instances, TextRef? Problem);

/// <summary>Was die UI für eine ausgewählte Instanz schickt: Name, Loader und Version dürfen geändert werden.</summary>
public sealed record ForeignImportChoice(string Id, string Name, LoaderType Loader, string MinecraftVersion, ForeignImportParts Parts);

/// <summary>Was mitkommen soll. Mods schließen ihre Konfiguration und Daten-Ordner (Minimap usw.) ein.</summary>
public sealed record ForeignImportParts(bool Worlds = true, bool Mods = true, bool Packs = true, bool Settings = true, bool Screenshots = true);

public sealed record ForeignImportResult(string Name, string? InstanceId, IReadOnlyList<TextRef> Notes, TextRef? Error);
