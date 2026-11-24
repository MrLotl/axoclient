using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.Instances.Contract;

namespace AxoClient.Content
{
    /// <summary>Sucht Updates für installierte Inhalte (Modrinth per Hash, viele auf einmal).</summary>
    public sealed class UpdateScanner : IUpdateScanner
    {
        private readonly IInstanceManager _instances;
        private readonly IContentManager _content;
        private readonly IModrinthProvider _modrinth;

        public UpdateScanner(IInstanceManager instances, IContentManager content, IModrinthProvider modrinth)
        {
            _instances = instances;
            _content = content;
            _modrinth = modrinth;
        }

        public async Task<IReadOnlyList<ContentUpdate>> Scan(string instanceId, CancellationToken ct)
        {
            var instance = await _instances.Get(instanceId, ct);
            var doc = await _content.Sync(instanceId, ct);
            var updates = new List<ContentUpdate>();
            foreach (var type in Enum.GetValues<ContentType>())
            {
                var entries = doc.ListOf(type)
                    .Where(e => e.Source == ContentSource.Modrinth && e.Hashes.Sha1 != null)
                    // Gesperrte Mods (Mod-Schutz) bekommen keine Updates angeboten
                    .Where(e => type != ContentType.Mod || !instance.ModLock.IsLocked(e))
                    .ToList();
                if (entries.Count == 0)
                {
                    continue;
                }

                var latest = await _modrinth.LatestForHashes(entries.Select(e => e.Hashes.Sha1!).ToList(),
                    instance.Minecraft.Version, instance.Loader.Type, _content.ToKind(type), ct);
                foreach (var entry in entries)
                {
                    if (!latest.TryGetValue(entry.Hashes.Sha1!, out var version) || version.Id == entry.VersionId)
                    {
                        continue;
                    }

                    if (version.PrimaryFile?.Sha1 is { } sha1 && sha1.Equals(entry.Hashes.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    updates.Add(new ContentUpdate(type, entry.File, entry.Name, entry.Version, version.VersionNumber, version.Id,
                        "modrinth", version.ProjectId, entry.IconUrl));
                }
            }
            return updates;
        }

        public async Task Apply(string instanceId, IReadOnlyList<ContentUpdate> updates, JobContext job)
        {
            for (var i = 0; i < updates.Count; i++)
            {
                var update = updates[i];
                job.Report((double)i / updates.Count, new TextRef("content.updating", ("name", update.Name)));
                await _content.Install(instanceId, update.Type, new InstallRequest(update.Source, update.ProjectId, update.NewVersionId), job);
            }
            job.Report(1);
        }
    }
}
