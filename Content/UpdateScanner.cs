namespace AxoClient.Content;

public class PendingUpdate(Installation installation, InstalledItem item) : Observable
{
    public Installation Installation { get; } = installation;
    public InstalledItem Item { get; } = item;

    public string Title => Item.DisplayName;
    public string InstanceName => Installation.Name;
    public string Where => $"{ContentTypes.Label(Item.Type)} in {InstanceName}";

    public string Change => Item.Entry?.VersionName is { Length: > 0 } from
        ? $"{from}  →  {Item.Update?.Name}"
        : $"neu: {Item.Update?.Name}";

    private bool _done;

    public bool IsDone
    {
        get => _done;
        set { _done = value; Changed(nameof(IsDone), nameof(StateText), nameof(CanUpdate)); }
    }

    private string _state = "";

    public string StateText
    {
        get => _state.Length > 0 ? _state : IsDone ? "Aktualisiert" : "";
        set { _state = value; Changed(); }
    }

    public bool CanUpdate => !IsDone;
}

public record UpdateScanResult(List<PendingUpdate> Updates, List<string> Skipped)
{
    public int InstanceCount => Updates.Select(u => u.Installation.Id).Distinct().Count();
}

public static class UpdateScanner
{
    public static async Task<UpdateScanResult> ScanAsync(AppServices app, IReadOnlyList<Installation> instances,
        WorkProgress progress)
    {
        var updates = new List<PendingUpdate>();
        var skipped = new List<string>();
        var done = 0;

        foreach (var inst in instances)
        {
            progress.Cancel.ThrowIfCancellationRequested();
            progress.Text.Report($"Prüfe \"{inst.Name}\"... ({done + 1} von {instances.Count})");
            try
            {
                var store = app.ContentOf(inst);
                foreach (var type in ContentTypes.All.Where(t => t != ContentType.Mod || inst.CanUseMods))
                {
                    progress.Cancel.ThrowIfCancellationRequested();
                    await store.IdentifyUnknownAsync(type);
                    var items = store.GetInstalled(type);
                    if (items.Count == 0)
                        continue;
                    await store.CheckUpdatesAsync(items);
                    updates.AddRange(items.Where(i => i.HasUpdate).Select(i => new PendingUpdate(inst, i)));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                skipped.Add($"{inst.Name}: {ErrorReport.Short(ex)}");
            }

            done++;
            progress.Fraction.Report((double)done / Math.Max(instances.Count, 1));
        }

        return new UpdateScanResult(
            updates.OrderBy(u => u.InstanceName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(u => u.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            skipped);
    }

    public static async Task<List<string>> ApplyAsync(AppServices app, IReadOnlyList<PendingUpdate> updates,
        WorkProgress progress)
    {
        var failed = new List<string>();
        var done = 0;

        foreach (var pending in updates)
        {
            progress.Cancel.ThrowIfCancellationRequested();
            var title = pending.Item.Entry?.Title ?? pending.Title;
            progress.Text.Report($"Aktualisiere {title} in \"{pending.InstanceName}\"... ({done + 1} von {updates.Count})");
            try
            {
                await app.ContentOf(pending.Installation).ApplyUpdateAsync(pending.Item, progress.Text);
                pending.IsDone = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                pending.StateText = "Fehlgeschlagen";
                failed.Add($"{title} in \"{pending.InstanceName}\": {ErrorReport.Short(ex)}");
            }
            done++;
            progress.Fraction.Report((double)done / Math.Max(updates.Count, 1));
        }
        return failed;
    }
}
