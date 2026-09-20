using System.Text.Json.Serialization;

namespace McLauncher;

public enum LoaderType
{
    Vanilla,
    Fabric,
    Forge
}

/// <summary>Eine Installation: Minecraft-Version + Mod-Loader + eigener Spielordner (Welten, Mods, Optionen).</summary>
public class Installation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public LoaderType Loader { get; set; } = LoaderType.Vanilla;
    public string MinecraftVersion { get; set; } = "26.2";

    /// <summary>Feste Version des Mod-Loaders (z.B. aus einem Modpack); null = jeweils die neueste passende.</summary>
    public string? LoaderVersion { get; set; }

    public string GameDir { get; set; } = "";

    /// <summary>Eigener Arbeitsspeicher (MB) für diese Instanz; null = Wert aus den Launcher-Einstellungen.</summary>
    public int? MaxRamMb { get; set; }

    // Statistik (wird beim Spielen gepflegt)
    public long PlayTimeSeconds { get; set; }
    public int LaunchCount { get; set; }
    public DateTime? LastPlayedUtc { get; set; }
    public int CrashCount { get; set; }
    public DateTime? LastCrashUtc { get; set; }

    /// <summary>Eigenes Bild (Dateiname unter ".axoclient/icons"); null = automatisch das Weltbild.</summary>
    public string? IconFile { get; set; }

    [JsonIgnore]
    public string Description => $"{Loader} · {MinecraftVersion}";

    [JsonIgnore]
    public string LoaderInitial => Loader.ToString()[..1];
}
