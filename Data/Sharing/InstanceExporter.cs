using System.IO;
using System.Text.Json;

namespace McLauncher;

/// <summary>Was in eine geteilte Instanz aufgenommen werden soll.</summary>
public record ExportOptions(bool Mods, bool ResourcePacks, bool Shaders, bool Servers, bool Options, bool Overlay);

/// <summary>Das fertige Paket und Hinweise, was nicht aufgenommen werden konnte.</summary>
public record ExportResult(InstanceManifest Manifest, List<string> Notes);

/// <summary>Baut aus einer Instanz das Paket, das an Freunde geht oder als Datei gespeichert wird.</summary>
public class InstanceExporter(AppState app)
{
    private const string LastServerPrefix = "lastServer:";

    public async Task<ExportResult> BuildAsync(Installation inst, ExportOptions options, WorkProgress progress)
    {
        var store = new ContentStore(inst, app.Http);
        var modrinth = new ModrinthProvider(app.Http);
        var notes = new List<string>();
        var manifest = new InstanceManifest
        {
            Name = inst.Name,
            Minecraft = inst.MinecraftVersion,
            Loader = inst.Loader,
            LoaderVersion = inst.LoaderVersion
        };

        var wanted = new[]
        {
            (Selected: options.Mods && inst.Loader != LoaderType.Vanilla, Type: ContentType.Mod, Label: "Mods"),
            (Selected: options.ResourcePacks, Type: ContentType.ResourcePack, Label: "Ressourcenpakete"),
            (Selected: options.Shaders, Type: ContentType.Shader, Label: "Shader")
        };
        foreach (var (selected, type, label) in wanted)
        {
            if (!selected)
                continue;
            progress.Cancel.ThrowIfCancellationRequested();
            progress.Text.Report($"Lese {label}...");
            AddContent(manifest, notes, await ReadItemsAsync(store, modrinth, type), type, label);
        }

        if (options.Servers)
        {
            manifest.Servers = new ServerStore(inst.GameDir).Load()
                .Where(s => s.Address.Trim().Length > 0)
                .Select(s => new ManifestServer { Name = s.Name, Address = s.Address.Trim() })
                .ToList();
        }

        if (options.Options)
        {
            var text = ReadOptions(inst);
            if (text == null)
                notes.Add("Einstellungen: In dieser Instanz gibt es noch keine gespeicherten Einstellungen.");
            else
                manifest.Options = text;
        }

        if (options.Overlay)
        {
            if (OverlayConfigFile.Read(inst) is { } overlay)
                manifest.Overlay = overlay;
            else
                notes.Add("Overlay: Für diese Instanz sind noch keine Overlay-Einstellungen gespeichert.");
        }

        manifest.Validate(); // ein Paket, das der Empfänger ablehnen würde, gar nicht erst verschicken
        return new ExportResult(manifest, notes);
    }

    /// <summary>
    /// Liest die installierten Dateien. Manuell hinzugefügte werden vorher über ihren Hash bei Modrinth gesucht,
    /// damit auch sie teilbar sind; ohne Netz bleibt es bei dem, was der Launcher schon kennt.
    /// </summary>
    private static async Task<List<InstalledItem>> ReadItemsAsync(ContentStore store, ModrinthProvider modrinth, ContentType type)
    {
        var items = store.GetInstalled(type);
        if (items.All(i => i.Entry != null))
            return items;
        try
        {
            if (await Task.Run(() => store.IdentifyUnknownAsync(type, modrinth)) > 0)
                items = store.GetInstalled(type);
        }
        catch
        {
            // offline oder Modrinth nicht erreichbar
        }
        return items;
    }

    private static void AddContent(InstanceManifest manifest, List<string> notes, List<InstalledItem> items,
        ContentType type, string label)
    {
        var skipped = new List<string>();
        foreach (var item in items)
        {
            if (item.Entry is { Source: ContentSource.Modrinth } entry)
            {
                manifest.Content.Add(new ManifestContent
                {
                    Type = type,
                    ProjectId = entry.ProjectId,
                    VersionId = entry.VersionId,
                    Title = entry.Title,
                    VersionName = entry.VersionName,
                    Enabled = item.Enabled
                });
            }
            else
            {
                skipped.Add(item.FileName);
            }
        }

        if (skipped.Count > 0)
            notes.Add($"{label}: {skipped.Count} nicht aufgenommen, weil sie nicht von Modrinth stammen und sich " +
                      $"nicht zuordnen ließen ({string.Join(", ", skipped.Take(5))}{(skipped.Count > 5 ? ", ..." : "")}).");
    }

    /// <summary>Die options.txt ohne die zuletzt besuchte Serveradresse (die geht Freunde nichts an).</summary>
    private static string? ReadOptions(Installation inst)
    {
        var path = Path.Combine(inst.GameDir, "options.txt");
        if (!File.Exists(path))
            return null;
        try
        {
            return string.Join("\n", File.ReadAllLines(path)
                .Where(line => !line.StartsWith(LastServerPrefix, StringComparison.Ordinal)));
        }
        catch (IOException)
        {
            return null; // gerade vom Spiel geschrieben
        }
    }
}
