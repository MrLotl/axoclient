using System.IO;
using System.Net.Http;

namespace McLauncher;

[Flags]
public enum TransferItems
{
    None = 0,
    Worlds = 1,
    Options = 2,
    Servers = 4,
    Shaders = 8,
    ResourcePacks = 16,
    Mods = 32
}

/// <summary>Woher übertragen wird: eine Instanz oder der offizielle Minecraft Launcher (.minecraft).</summary>
public class TransferSource
{
    public required string Name { get; init; }
    public required string GameDir { get; init; }
    public Installation? Installation { get; init; }

    public string Description => Installation?.Description ?? "Version und Mod-Loader unbekannt";

    public static string OfficialLauncherDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    /// <summary>Alle Instanzen (außer dem Ziel) und, falls vorhanden, der offizielle Launcher.</summary>
    public static List<TransferSource> Available(LauncherSettings settings, Installation? except)
    {
        var sources = settings.Installations
            .Where(i => i != except && Directory.Exists(i.GameDir))
            .Select(i => new TransferSource { Name = i.Name, GameDir = i.GameDir, Installation = i })
            .ToList();
        if (Directory.Exists(OfficialLauncherDir))
            sources.Add(new TransferSource { Name = "Offizieller Minecraft Launcher", GameDir = OfficialLauncherDir });
        return sources;
    }
}

/// <summary>Kopiert Welten, Einstellungen, Server, Shader, Ressourcenpakete und Mods zwischen Spielordnern.</summary>
public class InstanceTransfer(HttpClient http, string? curseForgeKey)
{
    // Einstellungsdateien: Vanilla (inkl. Tastenbelegung), OptiFine und Shader-Mods
    private static readonly string[] OptionFiles = ["options.txt", "optionsof.txt", "optionsshaders.txt"];
    private static readonly string[] ShaderConfigFiles = ["config/iris.properties", "config/oculus.properties"];

    /// <summary>Überträgt die gewählten Daten und liefert einen lesbaren Bericht.</summary>
    public async Task<List<string>> TransferAsync(TransferSource source, Installation target, TransferItems items,
        IProgress<string> status)
    {
        var report = new List<string>();
        var from = source.GameDir;
        var to = target.GameDir;
        Directory.CreateDirectory(to);

        if (items.HasFlag(TransferItems.Worlds))
        {
            status.Report("Kopiere Welten...");
            var count = await Task.Run(() => CopyWorlds(from, to));
            report.Add($"Welten: {count} kopiert.");
        }

        if (items.HasFlag(TransferItems.Options))
        {
            status.Report("Kopiere Einstellungen...");
            var copied = OptionFiles.Count(f => CopyFile(Path.Combine(from, f), Path.Combine(to, f), backup: true));
            report.Add(copied > 0
                ? "Einstellungen und Tastenbelegung übernommen."
                : "Einstellungen: In der Quelle gibt es noch keine options.txt.");
        }

        if (items.HasFlag(TransferItems.Servers))
        {
            status.Report("Übernehme Server...");
            var added = new ServerStore(to).Merge(new ServerStore(from).Load());
            report.Add($"Server: {added} neu hinzugefügt (bereits vorhandene Adressen übersprungen).");
        }

        if (items.HasFlag(TransferItems.ResourcePacks))
        {
            status.Report("Kopiere Ressourcenpakete...");
            var count = await Task.Run(() => CopyContent(source, target, ContentType.ResourcePack));
            report.Add($"Ressourcenpakete: {count} kopiert.");
        }

        if (items.HasFlag(TransferItems.Shaders))
        {
            status.Report("Kopiere Shader...");
            var count = await Task.Run(() => CopyContent(source, target, ContentType.Shader));
            foreach (var file in ShaderConfigFiles)
                CopyFile(Path.Combine(from, file), Path.Combine(to, file), backup: true);
            report.Add($"Shader: {count} kopiert." + (target.Loader == LoaderType.Vanilla
                ? " Hinweis: Vanilla kann keine Shader laden, dafür braucht es Iris (Fabric) oder Oculus (Forge)."
                : ""));
        }

        if (items.HasFlag(TransferItems.Mods))
            report.AddRange(await TransferModsAsync(source, target, status));

        status.Report("Übertragung abgeschlossen.");
        return report;
    }

    private static int CopyWorlds(string from, string to)
    {
        var saves = Path.Combine(from, "saves");
        if (!Directory.Exists(saves))
            return 0;
        var targetSaves = Path.Combine(to, "saves");
        Directory.CreateDirectory(targetSaves);
        var count = 0;
        foreach (var world in Directory.GetDirectories(saves).Where(d => File.Exists(Path.Combine(d, "level.dat"))))
        {
            // Gleichnamige Welten nicht überschreiben, sondern als "Welt (2)" ablegen
            var target = WorldStore.UniqueDir(targetSaves, Path.GetFileName(world));
            WorldStore.CopyDirectory(world, target, overwrite: false);
            File.Delete(Path.Combine(target, "session.lock")); // Sperrdatei einer evtl. laufenden Sitzung
            count++;
        }
        return count;
    }

    private static bool CopyFile(string from, string to, bool backup)
    {
        if (!File.Exists(from))
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (backup && File.Exists(to))
            File.Copy(to, to + ".bak", overwrite: true);
        File.Copy(from, to, overwrite: true);
        return true;
    }

    /// <summary>Kopiert Ressourcenpakete oder Shader (Dateien und Ordner) und übernimmt deren Herkunftsinfos.</summary>
    private int CopyContent(TransferSource source, Installation target, ContentType type)
    {
        var sourceVersion = source.Installation?.MinecraftVersion ?? "26.2";
        var fromDir = Path.Combine(source.GameDir, ContentTypes.Folder(type, sourceVersion));
        var targetStore = new ContentStore(target, http);
        var toDir = targetStore.FolderOf(type);
        if (!Directory.Exists(fromDir))
            return 0;
        Directory.CreateDirectory(toDir);

        var copied = new List<string>();
        foreach (var file in Directory.GetFiles(fromDir))
        {
            var dest = Path.Combine(toDir, Path.GetFileName(file));
            if (File.Exists(dest))
                continue;
            File.Copy(file, dest);
            copied.Add(Path.GetFileName(file));
        }
        foreach (var dir in Directory.GetDirectories(fromDir))
        {
            var dest = Path.Combine(toDir, Path.GetFileName(dir));
            if (Directory.Exists(dest))
                continue;
            WorldStore.CopyDirectory(dir, dest, overwrite: false);
            copied.Add(Path.GetFileName(dir));
        }

        CopyIndexEntries(source, targetStore, type, copied);
        return copied.Count;
    }

    private void CopyIndexEntries(TransferSource source, ContentStore targetStore, ContentType type, List<string> fileNames)
    {
        if (source.Installation == null || fileNames.Count == 0)
            return;
        var names = fileNames.Select(n => n.EndsWith(".disabled") ? n[..^".disabled".Length] : n).ToHashSet();
        var entries = new ContentStore(source.Installation, http).GetIndex()
            .Where(e => e.Type == type && names.Contains(e.FileName));
        targetStore.AddIndexEntries(entries);
    }

    /// <summary>
    /// Gleicher Loader + gleiche Version: Mods werden 1:1 kopiert (inkl. config).
    /// Sonst: über den Launcher installierte Mods werden für das Ziel neu heruntergeladen, manuelle Mods übersprungen.
    /// </summary>
    private async Task<List<string>> TransferModsAsync(TransferSource source, Installation target, IProgress<string> status)
    {
        if (target.Loader == LoaderType.Vanilla)
            return ["Mods: übersprungen, weil die Ziel-Instanz Vanilla ist (kein Mod-Loader)."];

        var fromDir = Path.Combine(source.GameDir, "mods");
        if (!Directory.Exists(fromDir))
            return ["Mods: In der Quelle gibt es keine Mods."];

        var src = source.Installation;
        var targetStore = new ContentStore(target, http);
        var sameSetup = src == null || (src.Loader == target.Loader && src.MinecraftVersion == target.MinecraftVersion);

        if (sameSetup)
        {
            status.Report("Kopiere Mods...");
            var toDir = targetStore.FolderOf(ContentType.Mod);
            Directory.CreateDirectory(toDir);
            var copied = new List<string>();
            foreach (var file in Directory.GetFiles(fromDir, "*.jar*"))
            {
                var dest = Path.Combine(toDir, Path.GetFileName(file));
                if (File.Exists(dest))
                    continue;
                File.Copy(file, dest);
                copied.Add(Path.GetFileName(file));
            }
            // Mod-Einstellungen mitnehmen, vorhandene im Ziel nicht überschreiben
            var config = Path.Combine(source.GameDir, "config");
            if (Directory.Exists(config))
                await Task.Run(() => WorldStore.CopyDirectory(config, Path.Combine(target.GameDir, "config"), overwrite: false));
            CopyIndexEntries(source, targetStore, ContentType.Mod, copied);

            return [src == null
                ? $"Mods: {copied.Count} unverändert kopiert. Achtung: Beim offiziellen Launcher ist die Version unbekannt, " +
                  $"die Mods müssen zu {target.Loader} {target.MinecraftVersion} passen."
                : $"Mods: {copied.Count} kopiert (inkl. Mod-Einstellungen)."];
        }

        // Andere Version oder anderer Loader: passende Dateien neu besorgen
        var sourceIndex = new ContentStore(src!, http).GetIndex().Where(e => e.Type == ContentType.Mod).ToList();
        var sourceFiles = Directory.GetFiles(fromDir, "*.jar*")
            .Select(f => Path.GetFileName(f).Replace(".disabled", ""))
            .ToList();
        var known = sourceIndex.Where(e => sourceFiles.Contains(e.FileName)).ToList();
        var unknown = sourceFiles.Where(f => known.All(k => k.FileName != f)).ToList();

        var installed = new List<string>();
        var failed = new List<string>();
        foreach (var mod in known)
        {
            IContentProvider? provider = mod.Source == ContentSource.Modrinth
                ? new ModrinthProvider(http)
                : string.IsNullOrWhiteSpace(curseForgeKey) ? null : new CurseForgeProvider(http, curseForgeKey);
            if (provider == null)
            {
                failed.Add($"{mod.Title} (CurseForge-Schlüssel fehlt)");
                continue;
            }
            try
            {
                await targetStore.InstallAsync(provider, mod.ProjectId, mod.Title, ContentType.Mod, status);
                installed.Add(mod.Title);
            }
            catch (Exception ex)
            {
                failed.Add($"{mod.Title} ({ex.Message})");
            }
        }

        var lines = new List<string>
        {
            $"Mods: {installed.Count} für {target.Loader} {target.MinecraftVersion} neu heruntergeladen " +
            "(andere Version/Loader als die Quelle, daher nicht einfach kopiert)."
        };
        if (failed.Count > 0)
            lines.Add("Nicht verfügbar: " + string.Join(", ", failed));
        if (unknown.Count > 0)
            lines.Add("Manuell hinzugefügte Mods übersprungen (Herkunft unbekannt): " + string.Join(", ", unknown));
        return lines;
    }
}
