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

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken)
        {
            var unknown = 0;
            foreach (var type in Enum.GetValues<ContentType>())
            {
                var directory = Path.Combine(context.GameDir, _contentFolders.Of(type));
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                var known = context.Content.ListOf(type).Select(entry => entry.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
                unknown += Directory.EnumerateFiles(directory).Select(Path.GetFileName)
                    .Count(fileName => _content.IsContentFile(type, fileName!) && !known.Contains(_content.BaseName(fileName!))
                                                                       && !_axoMod.IsAxoModFile(fileName!));
            }
            IReadOnlyList<CheckResult> results = unknown == 0
                ? [Ok()]
                : [new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.content.unknown", ("count", unknown)),
                    [new CheckAction("register", new TextRef("pipeline.action.register"), async (launchContext, job) =>
                        launchContext.Content = await _content.Sync(launchContext.Instance.Id, job.Token), true)])];
            return Task.FromResult(results);
        }
    }
}
