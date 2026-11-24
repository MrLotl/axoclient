using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Launch;
using AxoClient.Launch.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>Dieselbe Mod in zwei Versionen → Blocker "Neuere behalten".</summary>
    public sealed class DuplicateModCheck : LaunchCheck
    {
        private readonly IContentManager _content;
        private readonly IVersionRange _versionRange;

        public DuplicateModCheck(IContentManager content, IVersionRange versionRange)
        {
            _content = content;
            _versionRange = versionRange;
        }

        public override string Id => "duplicate";
        public override LaunchPhase Phase => LaunchPhase.Content;

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken)
        {
            var results = context.Content.Mods
                .Where(mod => mod.Enabled && mod.Meta?.ModId is { Length: > 0 } && mod.Source != ContentSource.AxoClient)
                .GroupBy(mod => mod.Meta!.ModId!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group =>
                {
                    var ordered = group.OrderByDescending(entry => entry.Version ?? "", Comparer<string>.Create(_versionRange.Compare))
                        .ThenByDescending(entry => entry.AddedAt).ToList();
                    var keep = ordered[0];
                    var older = ordered.Skip(1).Select(entry => entry.File).ToList();
                    return new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.content.duplicate", ("name", keep.Name),
                            ("count", ordered.Count)),
                        [new CheckAction("keepNewest", new TextRef("pipeline.action.keepNewest"), async (launchContext, job) =>
                        {
                            foreach (var file in older) { await _content.SetEnabled(launchContext.Instance.Id, ContentType.Mod, file, false, job.Token); } }, true)], CanSilence: false);
                }).ToList();
            return Task.FromResult<IReadOnlyList<CheckResult>>(results.Count == 0 ? [Ok()] : results);
        }
    }
}
