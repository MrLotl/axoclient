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

    /// <summary>Wie viele Anzeigen in den Einstellungen eingeschaltet sind (für die Vorschau).</summary>
    public static int CountEnabled(JsonElement config) =>
        config.ValueKind != JsonValueKind.Object
            ? 0
            : config.EnumerateObject().Count(p => p.Value.ValueKind == JsonValueKind.Object
                && p.Value.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True);
}
