using System.Text.Json.Serialization;
using AxoClient.Core.Common;

namespace AxoClient.Core.Content;

public enum ContentType
{
    Mod,
    ResourcePack,
    ShaderPack
}

public enum ContentSource
{
    Modrinth,
    [JsonStringEnumMemberName("curseforge")] CurseForge,
    Local,
    Friend,
    [JsonStringEnumMemberName("axoclient")] AxoClient
}

public sealed record ContentHashes(string? Sha1, string? Sha512);

/// <summary>Ein Eintrag in <c>content.json</c> (Mod, Ressourcepack oder Shader).</summary>
public sealed class ContentEntry
{
    public string File { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public bool Enabled { get; set; } = true;
    public ContentSource Source { get; set; } = ContentSource.Local;
    public string? ProjectId { get; set; }
    public string? VersionId { get; set; }
    public ContentHashes Hashes { get; set; } = new(null, null);
    public long Size { get; set; }
    public List<string> Dependencies { get; set; } = [];

    /// <summary>Mit welchem Stand des Lesers die Jar zuletzt gelesen wurde (auch wenn sie keine Metadaten hatte).</summary>
    public int MetaFormat { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Änderungszeit beim letzten Hashen: nur bei Änderung neu rechnen (Start-Pipeline).</summary>
    public DateTimeOffset? Modified { get; set; }

    /// <summary>Aus den Metadaten der Jar (fabric.mod.json, mods.toml …).</summary>
    public ModMetadata? Meta { get; set; }

    public string? IconUrl { get; set; }
    public string? Author { get; set; }

    [JsonIgnore]
    public string DiskName => Enabled ? File : File + ".disabled";
}

/// <summary>Das Nötigste aus den Metadaten einer Mod-Jar für die Prüfungen vor dem Start.</summary>
public sealed class ModMetadata
{
    public string? ModId { get; set; }
    public List<string> Loaders { get; set; } = [];

    /// <summary>Versionsbedingung für Minecraft, roh (z. B. "~1.21.4" oder "[1.20.1,1.21)").</summary>
    public string? MinecraftRange { get; set; }
    public string? LoaderRange { get; set; }

    /// <summary>Pflicht-Abhängigkeiten als Mod-IDs (ohne minecraft/java/loader).</summary>
    public List<string> Depends { get; set; } = [];
    public List<string> Breaks { get; set; } = [];
    public List<string> Conflicts { get; set; } = [];

    /// <summary>
    /// Versionsbedingung je Mod-ID aus <see cref="Breaks"/>/<see cref="Conflicts"/> (fehlt = jede Version). Nur ab
    /// <see cref="Format"/> 2 vorhanden; ältere Einträge kennen die Bedingung nicht.
    /// </summary>
    public Dictionary<string, string> BreakRanges { get; set; } = [];
    public Dictionary<string, string> ConflictRanges { get; set; } = [];

    /// <summary>Version laut Metadaten der Jar (nicht der Name der Modrinth-Version).</summary>
    public string? ModVersion { get; set; }

    /// <summary>Stand des Lesers; ältere Einträge werden beim nächsten Abgleich neu gelesen.</summary>
    public int Format { get; set; }

    /// <summary>3: alle Mod-IDs einer Forge-Jar, reine Server-Abhängigkeiten ausgelassen. 4: Container-Jars (Kotlin for Forge). 5: alle Loader einer Jar. 6: auch Loader, bei denen sich die Jar nur über META-INF/services anmeldet.</summary>
    public const int CurrentFormat = 6;

    /// <summary>IDs, die diese Jar zusätzlich bereitstellt (<c>provides</c> und mitgelieferte Jars).</summary>
    public List<string> Provides { get; set; } = [];

    /// <summary>Java-Pakete in der Jar, um Absturzursachen einer Mod zuzuordnen.</summary>
    public List<string> Packages { get; set; } = [];

    /// <summary>Mixin-Konfigurationen (z. B. "sodium.mixins.json").</summary>
    public List<string> Mixins { get; set; } = [];
}

/// <summary><c>instances/&lt;id&gt;/content.json</c></summary>
public sealed class ContentDocument : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public List<ContentEntry> Mods { get; set; } = [];
    public List<ContentEntry> Resourcepacks { get; set; } = [];
    public List<ContentEntry> Shaderpacks { get; set; } = [];
    public List<string> IgnoredWarnings { get; set; } = [];

    /// <summary>Wann sich Inhalte zuletzt geändert haben (Kompatibilität nur dann neu prüfen).</summary>
    public DateTimeOffset? ChangedAt { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }

    public List<ContentEntry> ListOf(ContentType type) => type switch
    {
        ContentType.Mod => Mods,
        ContentType.ResourcePack => Resourcepacks,
        _ => Shaderpacks
    };
}

public static class ContentFolders
{
    public static string Of(ContentType type) => type switch
    {
        ContentType.Mod => "mods",
        ContentType.ResourcePack => "resourcepacks",
        _ => "shaderpacks"
    };

    public static string ModrinthProjectType(ContentType type) => type switch
    {
        ContentType.Mod => "mod",
        ContentType.ResourcePack => "resourcepack",
        _ => "shader"
    };

    public static readonly string[] AllowedExtensions = [".jar", ".zip"];
}
