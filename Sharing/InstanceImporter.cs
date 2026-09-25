namespace AxoClient.Sharing;

public record ImportChoices(string Name, bool Servers, bool Options, bool Overlay);

public class InstanceImporter(AppServices app)
{
    private const int MaxListed = 8;

    public async Task ResolveTitlesAsync(InstanceManifest manifest)
    {
        if (manifest.Content.Count == 0)
            return;
        var projects = await app.Modrinth.GetProjectsAsync(manifest.Content.Select(c => c.ProjectId));
        foreach (var entry in manifest.Content)
            if (projects.TryGetValue(entry.ProjectId, out var project) && project.Title.Length > 0)
                entry.Title = Sanitize.Text(project.Title, 100);
    }

    public async Task<InstanceResult> ImportAsync(InstanceManifest manifest, ImportChoices choices, WorkProgress progress)
    {
        progress.Text.Report("Prüfe Minecraft-Version...");
        await VersionCatalog.EnsureAvailableAsync(app.Http, manifest.Loader, manifest.Minecraft,
            "diese Instanz kann nicht angelegt werden");

        var report = new List<string>();
        var inst = await app.Instances.CreateAsync(Sanitize.Text(choices.Name, 60), manifest.Loader, manifest.Minecraft,
            manifest.LoaderVersion, async created =>
            {
                WriteExtras(manifest, choices, created, report);
                await InstallContentAsync(manifest, created, progress, report);
                progress.Cancel.ThrowIfCancellationRequested();
            });

        report.Insert(0, $"Instanz \"{inst.Name}\" ({inst.Description}) wurde angelegt.");
        report.Add("Minecraft" + (inst.CanUseMods ? $" und {inst.Loader}" : "") +
                   " werden beim ersten Start automatisch geladen.");
        return new InstanceResult(inst, report);
    }

    private void WriteExtras(InstanceManifest manifest, ImportChoices choices, Installation inst, List<string> report)
    {
        if (choices.Servers && manifest.Servers.Count > 0)
        {
            new ServerStore(inst.GameDir).Save(manifest.Servers.Select(s => ServerStore.Create(s.Name, s.Address)));
            report.Add($"{manifest.Servers.Count} Server übernommen.");
        }

        if (choices.Options && manifest.Options != null)
        {
            File.WriteAllText(inst.OptionsFile, manifest.Options.Replace("\r\n", "\n"));
            report.Add("Einstellungen (options.txt) übernommen.");
        }

        if (choices.Overlay && manifest.Overlay is { } overlay)
        {
            new OverlayStore(inst).WriteActive(overlay);
            report.Add(BadgeMod.IsActiveFor(inst, app.Settings)
                ? "Overlay-Einstellungen übernommen."
                : "Overlay-Einstellungen übernommen, sie wirken aber nur mit Fabric und einer Minecraft-Version, " +
                  "für die AxoClient die Mod mitbringt.");
        }
    }

    private async Task InstallContentAsync(InstanceManifest manifest, Installation inst, WorkProgress progress,
        List<string> report)
    {
        var entries = manifest.Content
            .Where(c => c.Type != ContentType.Mod || inst.CanUseMods)
            .Select(c => new ContentStore.PinnedContent(c.Type, c.ProjectId, c.VersionId, c.Title, c.Enabled))
            .ToList();
        if (entries.Count == 0)
            return;

        var result = await app.ContentOf(inst).InstallPinnedAsync(entries, progress);
        report.Add($"{result.Installed} von {entries.Count} Inhalten installiert.");
        if (result.Replaced.Count > 0)
            report.Add("Diese Inhalte gab es nicht mehr in derselben Version (oder sie passen nicht zur Instanz), " +
                       $"deshalb wurde die neueste passende genommen: {Formats.Some(result.Replaced, MaxListed)}.");
        if (result.Failures.Count > 0)
            report.Add("Nicht installiert: " + Formats.Some(result.Failures, MaxListed, "; ") + ".");
    }
}
