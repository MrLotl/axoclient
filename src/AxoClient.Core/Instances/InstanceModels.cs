using AxoClient.Core.Common;
using AxoClient.Core.Game;

namespace AxoClient.Core.Instances;

public enum OriginType
{
    Empty,
    Copy,
    Import,
    Modpack
}

public sealed class InstanceOrigin
{
    public OriginType Type { get; set; } = OriginType.Empty;

    /// <summary>Bei Modpacks: Quelle und Projekt, damit Modpack-Updates gefunden werden.</summary>
    public string? Source { get; set; }
    public string? ProjectId { get; set; }
    public string? VersionId { get; set; }
    public string? VersionName { get; set; }

    /// <summary>Bei Kopien: von welcher Instanz.</summary>
    public string? CopiedFrom { get; set; }
}

public sealed class InstanceImages
{
    public string? Icon { get; set; }
    public string? Banner { get; set; }
    public bool BannerAuto { get; set; } = true;
}

/// <summary><c>instances/&lt;id&gt;/instance.json</c></summary>
public sealed class Instance : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public MinecraftVersionRef Minecraft { get; set; } = new("1.21.4", VersionType.Release);
    public LoaderRef Loader { get; set; } = new(LoaderType.Vanilla, null);

    /// <summary><c>null</c> = Standard aus den Launcher-Einstellungen.</summary>
    public int? MemoryMb { get; set; }

    /// <summary>Zusätzliche JVM-Argumente. Werden nie geteilt oder aus Importen übernommen.</summary>
    public string JavaArgs { get; set; } = "";
    public InstanceImages Images { get; set; } = new();
    public InstanceOrigin Origin { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>AxoClient-Mod in dieser Instanz verwenden (pro Instanz abschaltbar).</summary>
    public bool AxoMod { get; set; } = true;

    /// <summary>Mod-Schutz (frei, Pack geschützt, komplett gesperrt).</summary>
    public ModLock ModLock { get; set; } = new();
}

/// <summary>Was die Instanzliste der UI braucht.</summary>
public sealed record InstanceSummary(
    string Id,
    string Name,
    string MinecraftVersion,
    LoaderType Loader,
    string? LoaderVersion,
    string IconUrl,
    string? BannerUrl,
    bool HasIcon,
    DateTimeOffset? LastPlayed,
    long TotalSeconds,
    bool Running,
    int ModCount,
    DateTimeOffset CreatedAt,
    ModLockLevel ModLock);

public sealed record CreateInstanceRequest(
    string Name,
    string MinecraftVersion,
    LoaderType Loader,
    string? LoaderVersion,
    int? MemoryMb = null,
    string? CopyFrom = null);

public sealed record InstanceUpdate(
    string? Name = null,
    string? MinecraftVersion = null,
    LoaderType? Loader = null,
    string? LoaderVersion = null,
    int? MemoryMb = null,
    bool ResetMemory = false,
    string? JavaArgs = null,
    bool? BannerAuto = null,
    bool? AxoMod = null);

public static class InstanceRules
{
    public const int MaxNameLength = 60;

    public static string CleanName(string? name)
    {
        var clean = Text.Clean(name, MaxNameLength);
        return clean.Length == 0 ? throw new AxoException("instances.create.nameMissing") : clean;
    }
}
