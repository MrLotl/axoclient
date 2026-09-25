namespace AxoClient.Instances;

public enum UpgradeState
{
    Ready,
    Prerelease,
    Missing,
    Unknown
}

public class UpgradeEntry
{
    public required ContentType Type { get; init; }
    public required string ProjectId { get; init; }
    public required string Title { get; init; }
    public required UpgradeState State { get; init; }
    public ContentVersion? Version { get; init; }
    public bool Enabled { get; init; } = true;

    public string TypeText => ContentTypes.Label(Type);

    public string StateText => State switch
    {
        UpgradeState.Ready => Version!.Name,
        UpgradeState.Prerelease => $"{Version!.Name} ({Version.ChannelText})",
        UpgradeState.Missing => "keine passende Version",
        _ => "Herkunft unbekannt"
    };

    public bool CanCarry => State is UpgradeState.Ready or UpgradeState.Prerelease;
}

public record UpgradePlan(Installation Source, string TargetVersion, LoaderType TargetLoader, List<UpgradeEntry> Entries)
{
    public List<UpgradeEntry> Ready => Entries.Where(e => e.State == UpgradeState.Ready).ToList();
    public List<UpgradeEntry> Prereleases => Entries.Where(e => e.State == UpgradeState.Prerelease).ToList();
    public List<UpgradeEntry> Blocked => Entries.Where(e => !e.CanCarry).ToList();

    public string Summary => $"{Ready.Count} passen, {Prereleases.Count} nur als Beta/Alpha, {Blocked.Count} bleiben zurück";
}

public static class InstanceUpgrade
{
    public static async Task<UpgradePlan> PlanAsync(AppServices app, Installation source, string targetVersion,
        LoaderType targetLoader, WorkProgress progress)
    {
        var store = app.ContentOf(source);

        progress.Text.Report("Erkenne installierte Inhalte...");
        foreach (var type in ContentTypes.All)
        {
            progress.Cancel.ThrowIfCancellationRequested();
            try
            {
                await store.IdentifyUnknownAsync(type);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
            }
        }

        var probe = new Installation { MinecraftVersion = targetVersion, Loader = targetLoader, GameDir = source.GameDir };
        var installed = ContentTypes.All
            .Where(type => type != ContentType.Mod || source.CanUseMods)
            .SelectMany(store.GetInstalled)
            .ToList();

        var entries = new List<UpgradeEntry>();
        foreach (var item in installed)
        {
            progress.Cancel.ThrowIfCancellationRequested();
            progress.Text.Report($"Suche {item.DisplayName} für Minecraft {targetVersion}... " +
                                 $"({entries.Count + 1} von {installed.Count})");
            entries.Add(item.Entry is { Source: ContentSource.Modrinth } entry
                ? await PlanOneAsync(app.Modrinth, probe, item, entry)
                : Unknown(item, "", item.DisplayName));
            progress.Fraction.Report((double)entries.Count / Math.Max(installed.Count, 1));
        }

        return new UpgradePlan(source, targetVersion, targetLoader,
            entries.OrderBy(e => e.State).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    private static UpgradeEntry Unknown(InstalledItem item, string projectId, string title) => new()
    {
        Type = item.Type,
        ProjectId = projectId,
        Title = title,
        State = UpgradeState.Unknown,
        Enabled = item.Enabled
    };

    private static async Task<UpgradeEntry> PlanOneAsync(ModrinthClient modrinth, Installation probe,
        InstalledItem item, InstalledContent entry)
    {
        List<ContentVersion> versions;
        try
        {
            versions = await modrinth.GetVersionsAsync(entry.ProjectId, item.Type, probe);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Unknown(item, entry.ProjectId, entry.Title);
        }

        var release = versions.FirstOrDefault(v => v.IsRelease);
        var chosen = release ?? versions.FirstOrDefault();
        return new UpgradeEntry
        {
            Type = item.Type,
            ProjectId = entry.ProjectId,
            Title = entry.Title,
            Version = chosen,
            State = chosen == null ? UpgradeState.Missing
                : release != null ? UpgradeState.Ready
                : UpgradeState.Prerelease,
            Enabled = item.Enabled
        };
    }

    public static async Task<InstanceResult> ApplyAsync(AppServices app, UpgradePlan plan, string newName,
        IReadOnlyList<UpgradeEntry> carry, TransferItems transfer, WorkProgress progress)
    {
        var report = new List<string>();
        var inst = await app.Instances.CreateAsync(newName, plan.TargetLoader, plan.TargetVersion, null, async created =>
        {
            var plans = carry
                .Where(e => e is { CanCarry: true, Version: not null })
                .Select(e => new ContentStore.PlannedInstall(e.Type, e.ProjectId, e.Title, e.Version!, e.Enabled))
                .ToList();
            if (plans.Count > 0)
            {
                progress.Text.Report("Lade Inhalte in der neuen Version...");
                var failures = await app.ContentOf(created).InstallManyAsync(plans, progress);
                report.Add($"Inhalte: {plans.Count - failures.Count} von {plans.Count} geladen.");
                if (failures.Count > 0)
                    report.Add("Nicht geladen: " + string.Join(", ", failures));
            }

            if (transfer != TransferItems.None)
            {
                progress.Cancel.ThrowIfCancellationRequested();
                progress.Text.Report("Übernehme Welten und Einstellungen...");
                report.AddRange(await new InstanceTransfer(app.Modrinth).TransferAsync(TransferSource.Of(plan.Source),
                    created, transfer, progress.Text));
            }

            if (new OverlayStore(plan.Source).CopyTo(created) is > 0 and var profiles)
                report.Add($"Overlay: {profiles} Profil(e) übernommen.");

            var blocked = plan.Entries.Where(e => !carry.Contains(e)).ToList();
            if (blocked.Count > 0)
                report.Add("Nicht übernommen: " + string.Join(", ", blocked.Select(e => $"{e.Title} ({e.StateText})")));
        });

        report.Insert(0, $"Neue Instanz \"{inst.Name}\" ({plan.TargetLoader} {plan.TargetVersion}) angelegt. " +
                         $"\"{plan.Source.Name}\" bleibt unverändert.");
        return new InstanceResult(inst, report);
    }

    public static async Task<List<string>> TargetVersionsAsync(AppServices app, Installation inst, LoaderType loader)
    {
        var versions = await VersionCatalog.GetVersionsAsync(app.Http, loader, app.Settings.ShowSnapshots,
            app.Settings.ShowOldVersions);
        var current = versions.IndexOf(inst.MinecraftVersion);
        return current > 0 ? versions.Take(current).ToList() : versions;
    }
}
