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
    public string GameDir { get; set; } = "";

    /// <summary>Eigenes Bild (Dateiname unter ".axoclient/icons"); null = automatisch das Weltbild.</summary>
    public string? IconFile { get; set; }

    [JsonIgnore]
    public string Description => $"{Loader} · {MinecraftVersion}";

    [JsonIgnore]
    public string LoaderInitial => Loader.ToString()[..1];
}
