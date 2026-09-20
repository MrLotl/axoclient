using System.IO;

namespace McLauncher;

/// <summary>Schlägt anhand der Instanz (Loader, Mods, Shader, Ressourcenpakete) einen sinnvollen Arbeitsspeicher vor.</summary>
public static class RamAdvisor
{
    public sealed record Advice(int Mb, string Reason);

    /// <summary>Gesamter Arbeitsspeicher des Rechners in MB.</summary>
    public static int TotalMb() => (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);

    /// <summary>Aktuell verwendeter Wert der Instanz.</summary>
    public static int CurrentMb(Installation inst, LauncherSettings settings) => inst.MaxRamMb ?? settings.MaxRamMb;

    public static Advice Recommend(Installation inst)
    {
        var mods = Count(Path.Combine(inst.GameDir, "mods"), "*.jar");
        var shaders = Count(Path.Combine(inst.GameDir, "shaderpacks"), "*");
        var packs = Count(Path.Combine(inst.GameDir, "resourcepacks"), "*");

        var mb = inst.Loader == LoaderType.Vanilla ? 2048 : 3072;
        var parts = new List<string> { inst.Loader == LoaderType.Vanilla ? "Vanilla" : $"{inst.Loader}" };
        if (mods > 0)
        {
            mb += Math.Min(mods * 35, 5120);
            parts.Add($"{mods} Mod{(mods == 1 ? "" : "s")}");
        }
        if (shaders > 0)
        {
            mb += 2048;
            parts.Add("Shader");
        }
        if (packs > 0)
        {
            mb += 512;
            parts.Add("Ressourcenpakete");
        }

        // Nie mehr als 60 % des Rechners (Windows und Java brauchen selbst etwas), aber mindestens 2 GB
        var cap = Math.Max(2048, TotalMb() * 6 / 10 / 512 * 512);
        mb = Math.Clamp((mb + 255) / 512 * 512, 2048, Math.Min(cap, 16384));
        return new Advice(mb, string.Join(", ", parts));
    }

    public static string Format(int mb) => mb % 1024 == 0 ? $"{mb / 1024} GB" : $"{mb / 1024.0:0.#} GB";

    private static int Count(string folder, string pattern)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetFileSystemEntries(folder, pattern).Length : 0;
        }
        catch
        {
            return 0;
        }
    }
}
