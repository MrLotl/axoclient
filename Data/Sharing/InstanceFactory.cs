using System.IO;
using System.Text.Json;

namespace McLauncher;

/// <summary>Gemeinsame Handgriffe beim Anlegen von Instanzen (Editor, Import, Modpack).</summary>
public static class InstanceFactory
{
    /// <summary>Eigener, eindeutiger Spielordner für Welten, Mods und Optionen.</summary>
    public static string NewGameDir(string name, string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
        return Path.Combine(AppState.LauncherDir, "instances", $"{safeName}-{id}");
    }

    /// <summary>Hängt " (2)", " (3)" ... an, wenn es schon eine Instanz mit diesem Namen gibt.</summary>
    public static string UniqueName(LauncherSettings settings, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = "Importierte Instanz";
        var candidate = name;
        for (var n = 2; settings.Installations.Any(i => string.Equals(i.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} ({n})";
        return candidate;
    }

    /// <summary>Löscht den Ordner einer nicht fertig gewordenen Instanz (Fehler oder Abbruch), ohne selbst zu scheitern.</summary>
    public static void DiscardDirectory(string gameDir)
    {
        try
        {
            if (Directory.Exists(gameDir))
                Directory.Delete(gameDir, recursive: true);
        }
        catch
        {
            // z.B. eine Datei ist noch gesperrt; der Ordner bleibt dann liegen, das ist nur Platz
        }
    }
}

/// <summary>Die Overlay-Einstellungen einer Instanz (die Mod legt sie als config/axoclient-hud.json ab).</summary>
public static class OverlayConfigFile
{
    public static string PathOf(Installation inst) => Path.Combine(inst.GameDir, "config", "axoclient-hud.json");

    /// <summary>Die gespeicherten Einstellungen oder null, wenn es keine (brauchbaren) gibt.</summary>
    public static JsonElement? Read(Installation inst)
    {
        try
        {
            var path = PathOf(inst);
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Schreibt die Einstellungen; vorhandene bleiben als ".bak" erhalten.</summary>
    public static void Write(Installation inst, JsonElement config)
    {
        var path = PathOf(inst);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, config.GetRawText());
    }

    // ---------- Profile: config/axoclient-hud-profiles/<Name>.json ----------

    /// <summary>Höchstlänge eines Profilnamens (wie in der Mod).</summary>
    public const int MaxProfileName = 24;

    /// <summary>Dieses Profil gibt es immer; es gilt überall, wo kein anderes Profil eingestellt ist.</summary>
    public const string DefaultProfile = "Standard";

    /// <summary>Legt "Standard" an, falls es fehlt (aus den aktuellen Einstellungen, sonst leer = Standardwerte der Mod).</summary>
    public static void EnsureDefaultProfile(Installation inst)
    {
        try
        {
            var path = Path.Combine(ProfilesDir(inst), DefaultProfile + ".json");
            if (File.Exists(path))
                return;
            Directory.CreateDirectory(ProfilesDir(inst));
            File.WriteAllText(path, Read(inst)?.GetRawText() ?? "{}");
        }
        catch (IOException)
        {
            // nicht schreibbar: es geht auch ohne
        }
    }

    /// <summary>Name des Profils, das im Spiel gerade gilt (steht in den aktiven Einstellungen).</summary>
    public static string ActiveProfile(Installation inst)
    {
        try
        {
            if (Read(inst) is { } config && config.TryGetProperty("profil", out var name) && name.ValueKind == JsonValueKind.String
                && CleanProfileName(name.GetString()) is { Length: > 0 } clean)
                return clean;
        }
        catch (InvalidOperationException)
        {
            // kaputte Datei
        }
        return DefaultProfile;
    }

    /// <summary>Speichert ein Profil; gilt es gerade im Spiel, folgen auch die aktiven Einstellungen.</summary>
    public static void SaveProfile(Installation inst, string name, OverlayProfile profile)
    {
        var clean = CleanProfileName(name);
        if (clean.Length == 0)
            return;
        Directory.CreateDirectory(ProfilesDir(inst));
        File.WriteAllText(Path.Combine(ProfilesDir(inst), clean + ".json"), profile.ToJson());
        if (ActiveProfile(inst).Equals(clean, StringComparison.OrdinalIgnoreCase))
            WriteActive(inst, clean, profile);
    }

    /// <summary>Macht ein Profil zum aktiven: die Mod liest es beim nächsten Start.</summary>
    public static void Activate(Installation inst, string name)
    {
        var clean = CleanProfileName(name);
        var profile = ReadProfile(inst, clean);
        if (profile == null)
            return;
        WriteActive(inst, clean, OverlayProfile.Parse(profile.Value.GetRawText()));
    }

    private static void WriteActive(Installation inst, string name, OverlayProfile profile)
    {
        var copy = OverlayProfile.Parse(profile.ToJson());
        copy.Root["profil"] = name;
        var path = PathOf(inst);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, copy.ToJson());
    }

    public static string ProfilesDir(Installation inst) => Path.Combine(inst.GameDir, "config", "axoclient-hud-profiles");

    /// <summary>Nur Buchstaben, Ziffern, Leerzeichen, - und _ (es wird ein Dateiname); leer, wenn nichts übrig bleibt.</summary>
    public static string CleanProfileName(string? name)
    {
        var clean = new string((name ?? "").Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        return clean.Length > MaxProfileName ? clean[..MaxProfileName].Trim() : clean;
    }

    /// <summary>Namen der gespeicherten Profile, alphabetisch.</summary>
    public static List<string> ProfileNames(Installation inst)
    {
        try
        {
            var dir = ProfilesDir(inst);
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                    .OrderBy(n => n != DefaultProfile).ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList()
                : [];
        }
        catch
        {
            return [];
        }
    }

    public static JsonElement? ReadProfile(Installation inst, string name)
    {
        try
        {
            var clean = CleanProfileName(name);
            var path = Path.Combine(ProfilesDir(inst), clean + ".json");
            if (clean.Length == 0 || !File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Legt ein Profil an (ein gleichnamiges wird ersetzt). Liefert den tatsächlichen Namen.</summary>
    public static string WriteProfile(Installation inst, string name, JsonElement config)
    {
        var clean = CleanProfileName(name);
        if (clean.Length == 0)
            clean = "Profil";
        Directory.CreateDirectory(ProfilesDir(inst));
        File.WriteAllText(Path.Combine(ProfilesDir(inst), clean + ".json"), config.GetRawText());
        return clean;
    }

    public static bool DeleteProfile(Installation inst, string name)
    {
        if (CleanProfileName(name).Equals(DefaultProfile, StringComparison.OrdinalIgnoreCase))
            return false; // "Standard" bleibt immer
        try
        {
            var path = Path.Combine(ProfilesDir(inst), CleanProfileName(name) + ".json");
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Wie viele Anzeigen in den Einstellungen eingeschaltet sind (für die Vorschau).</summary>
    public static int CountEnabled(JsonElement config) =>
        config.ValueKind != JsonValueKind.Object
            ? 0
            : config.EnumerateObject().Count(p => p.Value.ValueKind == JsonValueKind.Object
                && p.Value.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True);
}
