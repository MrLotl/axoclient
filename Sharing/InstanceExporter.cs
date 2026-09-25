namespace AxoClient.Sharing;

public record ExportOptions(bool Mods, bool ResourcePacks, bool Shaders, bool Servers, bool Options, bool Overlay);

public record ExportResult(InstanceManifest Manifest, List<string> Notes);

public class InstanceExporter(AppServices app)
{
    private const string LastServerPrefix = "lastServer:";

    public async Task<ExportResult> BuildAsync(Installation inst, ExportOptions options, WorkProgress progress)
    {
        var store = app.ContentOf(inst);
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
            (Selected: options.Mods && inst.CanUseMods, Type: ContentType.Mod, Label: "Mods"),
            (Selected: options.ResourcePacks, Type: ContentType.ResourcePack, Label: "Ressourcenpakete"),
            (Selected: options.Shaders, Type: ContentType.Shader, Label: "Shader")
        };
        foreach (var (selected, type, label) in wanted.Where(w => w.Selected))
        {
            progress.Cancel.ThrowIfCancellationRequested();
            progress.Text.Report($"Lese {label}...");
            AddContent(manifest, notes, await ReadItemsAsync(store, type), type, label);
        }

        if (options.Servers)
            manifest.Servers = new ServerStore(inst.GameDir).Addresses()
                .Select(s => new ManifestServer { Name = s.Name, Address = s.Address })
                .ToList();

        if (options.Options)
        {
            if (ReadOptions(inst) is { } text)
                manifest.Options = text;
            else
                notes.Add("Einstellungen: In dieser Instanz gibt es noch keine gespeicherten Einstellungen.");
        }

        if (options.Overlay)
        {
            if (new OverlayStore(inst).ReadActive() is { } overlay)
                manifest.Overlay = overlay;
            else
                notes.Add("Overlay: Für diese Instanz sind noch keine Overlay-Einstellungen gespeichert.");
        }

        manifest.Validate();
        return new ExportResult(manifest, notes);
    }

    private static async Task<List<InstalledItem>> ReadItemsAsync(ContentStore store, ContentType type)
    {
        var items = store.GetInstalled(type);
        if (items.All(i => i.Entry != null))
            return items;
        try
        {
            if (await Task.Run(() => store.IdentifyUnknownAsync(type)) > 0)
                items = store.GetInstalled(type);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Mod-Angaben von Modrinth holen", ex);
        }
        return items;
    }

    private static void AddContent(InstanceManifest manifest, List<string> notes, List<InstalledItem> items,
        ContentType type, string label)
    {
        var skipped = new List<string>();
        foreach (var item in items)
        {
            if (item.Entry is not { IsFromModrinth: true } entry)
            {
                skipped.Add(item.FileName);
                continue;
            }
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

        if (skipped.Count > 0)
            notes.Add($"{label}: {skipped.Count} nicht aufgenommen, weil sie nicht von Modrinth stammen und sich " +
                      $"nicht zuordnen ließen ({string.Join(", ", skipped.Take(5))}{(skipped.Count > 5 ? ", ..." : "")}).");
    }

    private static string? ReadOptions(Installation inst)
    {
        if (!File.Exists(inst.OptionsFile))
            return null;
        try
        {
            return string.Join("\n", File.ReadAllLines(inst.OptionsFile)
                .Where(line => !line.StartsWith(LastServerPrefix, StringComparison.Ordinal)));
        }
        catch (IOException ex)
        {
            ErrorReport.Log("Datei für den Export lesen", ex);
            return null;
        }
    }
}
