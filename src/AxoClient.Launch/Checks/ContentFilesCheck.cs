using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Launch;

namespace AxoClient.Launch.Checks
{
    /// <summary>
    /// Abgleich mit <c>content.json</c>: fehlende oder beschädigte Dateien mit bekannter Quelle werden still neu geladen,
    /// lokale Dateien ohne Quelle nur gemeldet. Hashes nur, wenn sich Größe oder Änderungszeit geändert haben.
    /// </summary>
    public sealed class ContentFilesCheck : LaunchCheck
    {
        private readonly IContentProviders _providers;
        private readonly IDownloadManager _downloads;
        private readonly IContentManager _content;
        private readonly ITextHelper _textHelper;
        private readonly IPathGuard _pathGuard;
        private readonly IContentFolders _contentFolders;

        public ContentFilesCheck(
            IContentProviders providers,
            IDownloadManager downloads,
            IContentManager content,
            ITextHelper textHelper,
            IPathGuard pathGuard,
            IContentFolders contentFolders)
        {
            _providers = providers;
            _downloads = downloads;
            _content = content;
            _textHelper = textHelper;
            _pathGuard = pathGuard;
            _contentFolders = contentFolders;
        }

        public override string Id => "contentFiles";
        public override LaunchPhase Phase => LaunchPhase.Content;

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext ctx, CancellationToken ct)
        {
            var results = new List<CheckResult>();
            var redownload = new List<(ContentType Type, ContentEntry Entry)>();
            foreach (var type in Enum.GetValues<ContentType>())
            {
                foreach (var entry in ctx.Content.ListOf(type))
                {
                    if (entry.Source == ContentSource.AxoClient)
                    {
                        continue;
                    }

                    var path = Path.Combine(ctx.GameDir, _contentFolders.Of(type), entry.DiskName);
                    if (!File.Exists(path))
                    {
                        if (Directory.Exists(Path.Combine(ctx.GameDir, _contentFolders.Of(type), entry.File)))
                        {
                            continue;
                        }

                        if (hasSource(entry))
                        {
                            redownload.Add((type, entry));
                        }

                        continue;
                    }
                    var info = new FileInfo(path);
                    var changed = info.Length != entry.Size || entry.Modified != info.LastWriteTimeUtc;
                    if (!changed)
                    {
                        continue;
                    }

                    if (!ctx.Settings.Game.CheckModsOnLaunch && info.Length == entry.Size)
                    {
                        continue;
                    }

                    string sha1;
                    using (var stream = info.OpenRead())
                    {
                        sha1 = _textHelper.Sha1Hex(stream);
                    }

                    if (entry.Hashes.Sha1 == null || sha1.Equals(entry.Hashes.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (hasSource(entry))
                    {
                        redownload.Add((type, entry));
                    }
                    else if (type == ContentType.Mod && entry.Enabled)
                    {
                        results.Add(new CheckResult(Id, Severity.Warning, new TextRef("pipeline.content.localChanged", ("name", entry.Name)),
                        [DisableAction(_content, type, entry.File)], Key: $"hash:{entry.File}@{sha1[..8]}"));
                    }
                }
            }

            if (redownload.Count > 0)
            {
                results.Add(new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.content.redownload", ("count", redownload.Count)),
                    [new CheckAction("redownload", new TextRef("pipeline.action.redownload"), (c, job) => redownloadFiles(c, redownload, job), true)]));
            }

            return Task.FromResult<IReadOnlyList<CheckResult>>(results.Count == 0 ? [Ok()] : results);
        }

        private bool hasSource(ContentEntry e) =>
            e.Source is ContentSource.Modrinth or ContentSource.CurseForge && e.VersionId != null;

        private async Task redownloadFiles(LaunchContext ctx, List<(ContentType Type, ContentEntry Entry)> items, JobContext job)
        {
            var requests = new List<DownloadRequest>();
            foreach (var (type, entry) in items)
            {
                var version = await _providers.Get(entry.Source).GetVersion(entry.VersionId!, job.Token);
                var file = version?.Files.FirstOrDefault(f => f.FileName == entry.File) ?? version?.PrimaryFile;
                if (file == null || string.IsNullOrEmpty(file.Url))
                {
                    throw new AxoException("pipeline.content.redownloadFailed", new() { ["name"] = entry.Name });
                }

                requests.Add(new DownloadRequest(file.Url, _pathGuard.Combine(Path.Combine(ctx.GameDir, _contentFolders.Of(type)), entry.DiskName),
                    file.Sha1, file.Sha512));
            }
            job.Status("pipeline.content.downloading");
            await _downloads.DownloadAll(requests, job.Range(0, 1), job.Token);
        }
    }
}
