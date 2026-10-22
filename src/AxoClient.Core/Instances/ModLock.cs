using System.Text.Json.Serialization;
using AxoClient.Core.Common;
using AxoClient.Core.Content;

namespace AxoClient.Core.Instances;

/// <summary>Mod-Schutz einer Instanz. Betrifft nur Mods – Ressourcepacks, Shader, Welten und Server bleiben frei.</summary>
public enum ModLockLevel
{
    /// <summary>Frei: alle Mods lassen sich ändern.</summary>
    Off,

    /// <summary>Pack geschützt: die Mods des Packs bleiben, eigene lassen sich dazulegen und wieder entfernen.</summary>
    Pack,

    /// <summary>Komplett gesperrt: Mods lassen sich weder hinzufügen, entfernen, umschalten noch aktualisieren.</summary>
    Full
}

public sealed class ModLock
{
    public ModLockLevel Level { get; set; }

    /// <summary>Schlüssel der Mods, die beim Einschalten zum Pack gehörten (<see cref="ModLocks.KeyOf"/>).</summary>
    public List<string> PackMods { get; set; } = [];

    [JsonIgnore]
    public bool Active => Level != ModLockLevel.Off;
}

public static class ModLocks
{
    /// <summary>Projekt-Id, wo bekannt (übersteht Updates), sonst der Dateiname ohne <c>.disabled</c>.</summary>
    public static string KeyOf(ContentEntry entry) =>
        entry.ProjectId is { Length: > 0 } project ? "project:" + project : "file:" + entry.File.ToLowerInvariant();

    public static string FileKey(string fileName) => "file:" + ContentManager.BaseName(fileName).ToLowerInvariant();

    /// <summary>Gesperrt ist bei „Komplett“ jede Mod, bei „Pack“ nur, was beim Einschalten schon da war. Die AxoClient-Mod verwaltet der Launcher selbst.</summary>
    public static bool IsLocked(this ModLock modLock, ContentEntry entry) => entry.Source != ContentSource.AxoClient && modLock.Level switch
    {
        ModLockLevel.Full => true,
        ModLockLevel.Pack => modLock.PackMods.Contains(KeyOf(entry)) || modLock.PackMods.Contains("file:" + entry.File.ToLowerInvariant()),
        _ => false
    };

    public static bool CanAdd(this ModLock modLock) => modLock.Level != ModLockLevel.Full;

    public static void ThrowIfLocked(this ModLock modLock, ContentEntry entry)
    {
        if (modLock.IsLocked(entry))
            throw new AxoException("content.modLock.locked", new() { ["name"] = entry.Name });
    }

    public static void ThrowIfCannotAdd(this ModLock modLock)
    {
        if (!modLock.CanAdd())
            throw new AxoException("content.modLock.full");
    }

    /// <summary>
    /// Stellt die Stufe um. Beim Einschalten (oder mit <paramref name="adoptCurrent"/>) zählen alle Mods, die gerade
    /// in der Instanz liegen, zum Pack. Beim Wechsel zwischen „Pack“ und „Komplett“ bleibt die Liste, damit eigene Mods eigene bleiben.
    /// </summary>
    public static void SetLevel(this ModLock modLock, ModLockLevel level, IEnumerable<ContentEntry> currentMods, bool adoptCurrent = false)
    {
        if (level == ModLockLevel.Off)
            modLock.PackMods = [];
        else if (adoptCurrent || modLock.Level == ModLockLevel.Off || modLock.PackMods.Count == 0)
            modLock.PackMods = currentMods.Where(m => m.Source != ContentSource.AxoClient).Select(KeyOf).Distinct().Order().ToList();
        modLock.Level = level;
    }
}
