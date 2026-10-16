using AxoClient.Core.Common;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Content;

public sealed record ContentUpdate(ContentType Type, string File, string Name, string? CurrentVersion, string NewVersion,
    string NewVersionId, string Source, string ProjectId, string? IconUrl);

/// <summary>Sucht Updates für installierte Inhalte (Modrinth per Hash, viele auf einmal).</summary>
public sealed class UpdateScanner(InstanceManager instances, ContentManager content, ModrinthProvider modrinth)
{
    public async Task<IReadOnlyList<ContentUpdate>> ScanAsync(string instanceId, CancellationToken ct)
    {
        var instance = await instances.GetAsync(instanceId, ct);
        var doc = await content.SyncAsync(instanceId, ct);
        var updates = new List<ContentUpdate>();
        foreach (var type in Enum.GetValues<ContentType>())
        {
            var entries = doc.ListOf(type)
                .Where(e => e.Source == ContentSource.Modrinth && e.Hashes.Sha1 != null)
                .ToList();
            if (entries.Count == 0)
                continue;
            var latest = await modrinth.LatestForHashesAsync(entries.Select(e => e.Hashes.Sha1!).ToList(),
                instance.Minecraft.Version, instance.Loader.Type, ContentManager.ToKind(type), ct);
            foreach (var entry in entries)
            {
                if (!latest.TryGetValue(entry.Hashes.Sha1!, out var version) || version.Id == entry.VersionId)
                    continue;
                if (version.PrimaryFile?.Sha1 is { } sha1 && sha1.Equals(entry.Hashes.Sha1, StringComparison.OrdinalIgnoreCase))
                    continue;
                updates.Add(new ContentUpdate(type, entry.File, entry.Name, entry.Version, version.VersionNumber, version.Id,
                    "modrinth", version.ProjectId, entry.IconUrl));
            }
        }
        return updates;
    }

    public async Task ApplyAsync(string instanceId, IReadOnlyList<ContentUpdate> updates, JobContext job)
    {
        for (var i = 0; i < updates.Count; i++)
        {
            var update = updates[i];
            job.Report((double)i / updates.Count, TextRef.Of("content.updating", ("name", update.Name)));
            await content.InstallAsync(instanceId, update.Type, new InstallRequest(update.Source, update.ProjectId, update.NewVersionId), job);
        }
        job.Report(1);
    }
}
