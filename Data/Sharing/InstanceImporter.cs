using System.IO;

namespace McLauncher;

/// <summary>Was der Empfänger aus einem geteilten Paket übernehmen möchte.</summary>
public record ImportChoices(string Name, bool Servers, bool Options, bool Overlay);

public record ImportResult(Installation Instance, List<string> Report);

/// <summary>Legt aus einem geteilten Paket eine neue Instanz an und lädt alle Inhalte von Modrinth.</summary>
public class InstanceImporter(AppState app)
{
    /// <summary>
    /// Ersetzt die Namen im Paket durch die echten von Modrinth. Was in einer fremden Datei steht, ist nur Text
    /// von jemand anderem – so kann sich kein Eintrag als etwas Bekanntes ausgeben.
    /// </summary>
    public async Task ResolveTitlesAsync(InstanceManifest manifest)
    {
        if (manifest.Content.Count == 0)
            return;
        var titles = await new ModrinthProvider(app.Http).GetProjectTitlesAsync(manifest.Content.Select(c => c.ProjectId));
        foreach (var entry in manifest.Content)
            if (titles.TryGetValue(entry.ProjectId, out var title) && title.Length > 0)
                entry.Title = ShareValidation.CleanText(title, 100);
    }

    public async Task<ImportResult> ImportAsync(InstanceManifest manifest, ImportChoices choices, WorkProgress progress)
    {
        progress.Text.Report("Prüfe Minecraft-Version...");
        var versions = await VersionCatalog.GetVersionsAsync(app.Http, manifest.Loader, snapshots: true, oldVersions: true);
        if (!versions.Contains(manifest.Minecraft))
            throw new InvalidOperationException(
                $"{manifest.Loader} gibt es für Minecraft {manifest.Minecraft} nicht (mehr) – diese Instanz kann nicht angelegt werden.");

        var inst = new Installation
        {
            Name = InstanceFactory.UniqueName(app.Settings, ShareValidation.CleanText(choices.Name, 60)),
            Loader = manifest.Loader,
            MinecraftVersion = manifest.Minecraft,
            LoaderVersion = manifest.LoaderVersion
        };
        inst.GameDir = InstanceFactory.NewGameDir(inst.Name, inst.Id);

        var report = new List<string>();
        try
        {
            Directory.CreateDirectory(inst.GameDir);
            WriteExtras(manifest, choices, inst, report);
            await InstallContentAsync(manifest, inst, progress, report);
            progress.Cancel.ThrowIfCancellationRequested();
        }
        catch
        {
            InstanceFactory.DiscardDirectory(inst.GameDir); // Fehler oder Abbruch: keine halbe Instanz zurücklassen
            throw;
        }

        app.Settings.Installations.Add(inst);
        app.NotifyInstallationsChanged();

        report.Insert(0, $"Instanz \"{inst.Name}\" ({inst.Description}) wurde angelegt.");
        report.Add("Minecraft" + (inst.Loader == LoaderType.Vanilla ? "" : $" und {inst.Loader}") +
                   " werden beim ersten Start automatisch geladen.");
        return new ImportResult(inst, report);
    }

    /// <summary>Server, Einstellungen und Overlay: kleine Dateien, die ohne Netz geschrieben werden.</summary>
    private void WriteExtras(InstanceManifest manifest, ImportChoices choices, Installation inst, List<string> report)
    {
        if (choices.Servers && manifest.Servers.Count > 0)
        {
            new ServerStore(inst.GameDir).Save(manifest.Servers.Select(s => ServerStore.Create(s.Name, s.Address)));
            report.Add($"{manifest.Servers.Count} Server übernommen.");
        }

        if (choices.Options && manifest.Options != null)
        {
            File.WriteAllText(Path.Combine(inst.GameDir, "options.txt"), manifest.Options.Replace("\r\n", "\n"));
            report.Add("Einstellungen (options.txt) übernommen.");
        }

        if (choices.Overlay && manifest.Overlay is { } overlay)
        {
            OverlayConfigFile.Write(inst, overlay);
            report.Add(Badge.IsActiveFor(inst, app.Settings)
                ? "Overlay-Einstellungen übernommen."
                : "Overlay-Einstellungen übernommen, sie wirken aber nur mit Fabric und einer Minecraft-Version, " +
                  "für die AxoClient die Mod mitbringt.");
        }
    }

    private async Task InstallContentAsync(InstanceManifest manifest, Installation inst, WorkProgress progress,
        List<string> report)
    {
        // Mods gibt es ohne Mod-Loader nicht
        var entries = manifest.Content.Where(c => c.Type != ContentType.Mod || inst.Loader != LoaderType.Vanilla).ToList();
        if (entries.Count == 0)
            return;

        var store = new ContentStore(inst, app.Http);
        var modrinth = new ModrinthProvider(app.Http);

        progress.Text.Report("Suche die Versionen bei Modrinth...");
        var pinned = await modrinth.GetVersionsByIdsAsync(entries.Where(e => e.VersionId != null).Select(e => e.VersionId!));

        // Genau die Versionen des Absenders; gibt es sie nicht mehr oder passen sie nicht, die neueste passende
        var exact = new List<ContentStore.PlannedInstall>();
        var newest = new List<ManifestContent>();
        foreach (var entry in entries)
        {
            if (entry.VersionId != null && pinned.TryGetValue(entry.VersionId, out var version)
                && version.ProjectId == entry.ProjectId && version.Supports(inst, entry.Type))
                exact.Add(new ContentStore.PlannedInstall(entry.Type, entry.ProjectId, entry.Title, version,
                    entry.Enabled || entry.Type != ContentType.Mod)); // abschalten geht nur bei Mods
            else
                newest.Add(entry);
        }

        var failures = exact.Count > 0 ? await store.InstallManyAsync(exact, progress) : [];
        var installed = exact.Count - failures.Count;

        var replaced = new List<string>();
        for (var i = 0; i < newest.Count; i++)
        {
            var entry = newest[i];
            progress.Cancel.ThrowIfCancellationRequested();
            progress.Text.Report($"Lade {entry.Title} ({i + 1} von {newest.Count}, neueste Version)...");
            try
            {
                await store.InstallAsync(modrinth, entry.ProjectId, entry.Title, entry.Type, progress.Text);
                if (!entry.Enabled && entry.Type == ContentType.Mod)
                    DisableInstalled(store, entry.ProjectId);
                replaced.Add(entry.Title);
                installed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{entry.Title} ({ex.Message})");
            }
        }

        report.Add($"{installed} von {entries.Count} Inhalten installiert.");
        if (replaced.Count > 0)
            report.Add("Diese Inhalte gab es nicht mehr in derselben Version (oder sie passen nicht zur Instanz), " +
                       $"deshalb wurde die neueste passende genommen: {string.Join(", ", replaced.Take(8))}" +
                       (replaced.Count > 8 ? $" und {replaced.Count - 8} weitere" : "") + ".");
        if (failures.Count > 0)
            report.Add("Nicht installiert: " + string.Join("; ", failures.Take(8)) +
                       (failures.Count > 8 ? $" und {failures.Count - 8} weitere" : "") + ".");
    }

    private static void DisableInstalled(ContentStore store, string projectId)
    {
        if (store.GetInstalled(ContentType.Mod).FirstOrDefault(m => m.Entry?.ProjectId == projectId) is { } item)
            store.SetEnabled(item, false);
    }
}
