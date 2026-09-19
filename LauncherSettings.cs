using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McLauncher;

/// <summary>Launcher-Einstellungen, gespeichert als JSON im Launcher-Ordner.</summary>
public class LauncherSettings
{
    public List<Installation> Installations { get; set; } = [];
    public string? SelectedInstallationId { get; set; }
    public bool ShowSnapshots { get; set; }
    public bool ShowOldVersions { get; set; }
    public string? CurseForgeApiKey { get; set; }

    /// <summary>Instanzen als Kacheln statt als Liste anzeigen.</summary>
    public bool InstancesAsTiles { get; set; }

    /// <summary>Mods, Ressourcenpakete und Shader einer Instanz als Kacheln statt als Liste anzeigen.</summary>
    public bool ContentAsTiles { get; set; }

    // Axolotl-Symbol in der Tabliste (Badge-Mod + Cloudflare-Dienst)
    public bool BadgeEnabled { get; set; } = true;
    public const string DefaultBadgeApiUrl = "https://mclauncher-badge.bernhardtfinn0.workers.dev";
    public string? BadgeApiUrl { get; set; } = DefaultBadgeApiUrl;

    /// <summary>Anmelde-Token beim Dienst (für Freunde und Status) und für welchen Spieler es gilt.</summary>
    public string? BadgeToken { get; set; }
    public string? BadgeTokenUuid { get; set; }

    // Discord-Status "Spielt AxoClient" (Application-ID aus dem Discord Developer Portal)
    public bool DiscordEnabled { get; set; } = true;
    public const string DefaultDiscordAppId = "1550925254124118067";
    public string? DiscordAppId { get; set; } = DefaultDiscordAppId;

    // Spielstart
    public int MaxRamMb { get; set; } = 4096;
    public string JvmArguments { get; set; } = "";
    public int GameWidth { get; set; }  // 0 = Standard
    public int GameHeight { get; set; } // 0 = Standard
    public bool FullScreen { get; set; }
    public bool MinimizeOnLaunch { get; set; } = true;

    // Nur zum Übernehmen der Version aus älteren Launcher-Versionen (vor den Installationen)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string FilePath(string launcherDir) => Path.Combine(launcherDir, "launcher-settings.json");

    public static LauncherSettings Load(string launcherDir)
    {
        LauncherSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<LauncherSettings>(
                File.ReadAllText(FilePath(launcherDir)), JsonOptions) ?? new LauncherSettings();
        }
        catch
        {
            settings = new LauncherSettings();
        }

        if (settings.Installations.Count == 0)
        {
            // Erste Installation nutzt den bisherigen Spielordner, damit vorhandene Welten erhalten bleiben
            settings.Installations.Add(new Installation
            {
                Name = "Vanilla",
                Loader = LoaderType.Vanilla,
                MinecraftVersion = settings.Version ?? "26.2",
                GameDir = launcherDir
            });
        }
        settings.Version = null;

        // Leere Felder (z.B. aus älteren Versionen) bekommen die eingebauten AxoClient-Standardwerte
        if (string.IsNullOrWhiteSpace(settings.BadgeApiUrl))
            settings.BadgeApiUrl = DefaultBadgeApiUrl;
        if (string.IsNullOrWhiteSpace(settings.DiscordAppId))
            settings.DiscordAppId = DefaultDiscordAppId;

        // Nach dem Umzug von ".mclauncher" nach ".axoclient" zeigen die Instanz-Pfade noch auf den alten Ordner
        if (!string.Equals(launcherDir, AppState.OldLauncherDir, StringComparison.OrdinalIgnoreCase))
        {
            var changed = false;
            foreach (var inst in settings.Installations)
            {
                if (!inst.GameDir.StartsWith(AppState.OldLauncherDir, StringComparison.OrdinalIgnoreCase))
                    continue;
                var rest = inst.GameDir[AppState.OldLauncherDir.Length..];
                if (rest.Length > 0 && rest[0] is not ('\\' or '/'))
                    continue; // z.B. ".mclauncher-alt" – anderer Ordner
                inst.GameDir = launcherDir + rest;
                changed = true;
            }
            if (changed)
                settings.Save(launcherDir);
        }
        return settings;
    }

    public void Save(string launcherDir)
    {
        Directory.CreateDirectory(launcherDir);
        File.WriteAllText(FilePath(launcherDir), JsonSerializer.Serialize(this, JsonOptions));
    }
}
