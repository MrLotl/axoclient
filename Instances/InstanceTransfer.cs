namespace AxoClient.Instances;

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

public class TransferSource
{
    public required string Name { get; init; }
    public required string GameDir { get; init; }
    public Installation? Installation { get; init; }

    public string Description => Installation?.Description ?? "Version und Mod-Loader unbekannt";

    public string MinecraftVersion => Installation?.MinecraftVersion ?? LauncherSettings.DefaultMinecraftVersion;

    public string ContentDir(ContentType type) => Path.Combine(GameDir, ContentTypes.Folder(type, MinecraftVersion));

    public static TransferSource Of(Installation inst) => new() { Name = inst.Name, GameDir = inst.GameDir, Installation = inst };

    public static List<TransferSource> Available(IEnumerable<Installation> instances, Installation? except)
    {
        var sources = instances
            .Where(i => i != except && Directory.Exists(i.GameDir))
            .Select(Of)
            .ToList();
        if (Directory.Exists(AppPaths.OfficialMinecraftDir))
            sources.Add(new TransferSource { Name = "Offizieller Minecraft Launcher", GameDir = AppPaths.OfficialMinecraftDir });
        return sources;
    }
}

public class InstanceTransfer(ModrinthClient modrinth)
{
    private static readonly string[] OptionFiles = ["options.txt", "optionsof.txt", "optionsshaders.txt"];
    private static readonly string[] ShaderConfigFiles = ["config/iris.properties", "config/oculus.properties"];

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
            var count = await Task.Run(() => WorldStore.CopyAll(Path.Combine(from, "saves"), target.SavesDir));
            report.Add($"Welten: {count} kopiert.");
        }

        if (items.HasFlag(TransferItems.Options))
        {
            status.Report("Kopiere Einstellungen...");
            var copied = OptionFiles.Count(f => CopyWithBackup(Path.Combine(from, f), Path.Combine(to, f)));
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
                CopyWithBackup(Path.Combine(from, file), Path.Combine(to, file));
            report.Add($"Shader: {count} kopiert." + (target.CanUseMods
                ? ""
                : " Hinweis: Vanilla kann keine Shader laden, dafür braucht es Iris (Fabric) oder Oculus (Forge)."));
        }

        if (items.HasFlag(TransferItems.Mods))
            report.AddRange(await TransferModsAsync(source, target, status));

        status.Report("Übertragung abgeschlossen.");
        return report;
    }

    private static bool CopyWithBackup(string from, string to)
    {
        if (!File.Exists(from))
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (File.Exists(to))
            File.Copy(to, to + ".bak", overwrite: true);
        File.Copy(from, to, overwrite: true);
        return true;
    }

    private int CopyContent(TransferSource source, Installation target, ContentType type)
    {
        var fromDir = source.ContentDir(type);
        if (!Directory.Exists(fromDir))
            return 0;
        var toDir = target.ContentDir(type);
        Directory.CreateDirectory(toDir);

        var copied = new List<string>();
        foreach (var file in Directory.GetFiles(fromDir))
        {
            var destination = Path.Combine(toDir, Path.GetFileName(file));
            if (File.Exists(destination))
                continue;
            File.Copy(file, destination);
            copied.Add(Path.GetFileName(file));
        }
        foreach (var dir in Directory.GetDirectories(fromDir))
        {
            var destination = Path.Combine(toDir, Path.GetFileName(dir));
            if (Directory.Exists(destination))
                continue;
            FileOps.CopyDirectory(dir, destination, overwrite: false);
            copied.Add(Path.GetFileName(dir));
        }

        CopyIndexEntries(source, target, type, copied);
        return copied.Count;
    }

    private void CopyIndexEntries(TransferSource source, Installation target, ContentType type, List<string> fileNames)
    {
        if (source.Installation == null || fileNames.Count == 0)
            return;
        var names = fileNames.Select(ContentStore.EnabledName).ToHashSet();
        var entries = new ContentStore(source.Installation, modrinth).GetIndex()
            .Where(e => e.Type == type && names.Contains(e.FileName));
        new ContentStore(target, modrinth).AddIndexEntries(entries);
    }

    private async Task<List<string>> TransferModsAsync(TransferSource source, Installation target, IProgress<string> status)
    {
        if (!target.CanUseMods)
            return ["Mods: übersprungen, weil die Ziel-Instanz Vanilla ist (kein Mod-Loader)."];

        var fromDir = Path.Combine(source.GameDir, "mods");
        if (!Directory.Exists(fromDir))
            return ["Mods: In der Quelle gibt es keine Mods."];

        var src = source.Installation;
        var targetStore = new ContentStore(target, modrinth);
        if (src == null || (src.Loader == target.Loader && src.MinecraftVersion == target.MinecraftVersion))
        {
            status.Report("Kopiere Mods...");
            Directory.CreateDirectory(target.ModsDir);
            var copied = new List<string>();
            foreach (var file in Directory.GetFiles(fromDir, "*.jar*"))
            {
                var destination = Path.Combine(target.ModsDir, Path.GetFileName(file));
                if (File.Exists(destination))
                    continue;
                File.Copy(file, destination);
                copied.Add(Path.GetFileName(file));
            }
            var config = Path.Combine(source.GameDir, "config");
            if (Directory.Exists(config))
                await Task.Run(() => FileOps.CopyDirectory(config, target.ConfigDir, overwrite: false));
            CopyIndexEntries(source, target, ContentType.Mod, copied);

            return [src == null
                ? $"Mods: {copied.Count} unverändert kopiert. Achtung: Beim offiziellen Launcher ist die Version unbekannt, " +
                  $"die Mods müssen zu {target.Loader} {target.MinecraftVersion} passen."
                : $"Mods: {copied.Count} kopiert (inkl. Mod-Einstellungen)."];
        }

        var sourceIndex = new ContentStore(src, modrinth).GetIndex().Where(e => e.Type == ContentType.Mod).ToList();
        var sourceFiles = Directory.GetFiles(fromDir, "*.jar*").Select(f => ContentStore.EnabledName(Path.GetFileName(f))).ToList();
        var known = sourceIndex.Where(e => sourceFiles.Contains(e.FileName)).ToList();
        var unknown = sourceFiles.Where(f => known.All(k => k.FileName != f)).ToList();

        var installed = new List<string>();
        var failed = new List<string>();
        foreach (var mod in known)
        {
            if (mod.Source != ContentSource.Modrinth)
            {
                failed.Add($"{mod.Title} (nicht von Modrinth)");
                continue;
            }
            try
            {
                await targetStore.InstallAsync(mod.ProjectId, mod.Title, ContentType.Mod, status);
                installed.Add(mod.Title);
            }
            catch (Exception ex)
            {
                failed.Add($"{mod.Title} ({ErrorReport.Short(ex)})");
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
