using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Launch;
using AxoClient.Game.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>Jars in <c>mods/</c>, die nicht in content.json stehen → Auto-Fix: als <c>local</c> eintragen.</summary>
    public sealed class UnknownFilesCheck : LaunchCheck
    {
        private readonly IContentManager _content;
        private readonly IContentFolders _contentFolders;
        private readonly IAxoModInstaller _axoMod;

        public UnknownFilesCheck(IContentManager content, IContentFolders contentFolders, IAxoModInstaller axoMod)
        {
            _content = content;
            _contentFolders = contentFolders;
            _axoMod = axoMod;
        }

        public override string Id => "unknownFiles";
        public override LaunchPhase Phase => LaunchPhase.Content;

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext ctx, CancellationToken ct)
        {
            var unknown = 0;
            foreach (var type in Enum.GetValues<ContentType>())
            {
                var dir = Path.Combine(ctx.GameDir, _contentFolders.Of(type));
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                var known = ctx.Content.ListOf(type).Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
                unknown += Directory.EnumerateFiles(dir).Select(Path.GetFileName)
                    .Count(f => _content.IsContentFile(type, f!) && !known.Contains(_content.BaseName(f!))
                                                                       && !_axoMod.IsAxoModFile(f!));
            }
            IReadOnlyList<CheckResult> results = unknown == 0
                ? [Ok()]
                : [new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.content.unknown", ("count", unknown)),
                    [new CheckAction("register", new TextRef("pipeline.action.register"), async (c, job) =>
                        c.Content = await _content.Sync(c.Instance.Id, job.Token), true)])];
            return Task.FromResult(results);
        }
    }
}
