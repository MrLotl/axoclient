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

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext ctx, CancellationToken ct)
        {
            var results = ctx.Content.Mods
                .Where(m => m.Enabled && m.Meta?.ModId is { Length: > 0 } && m.Source != ContentSource.AxoClient)
                .GroupBy(m => m.Meta!.ModId!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g =>
                {
                    var ordered = g.OrderByDescending(m => m.Version ?? "", Comparer<string>.Create(_versionRange.Compare))
                        .ThenByDescending(m => m.AddedAt).ToList();
                    var keep = ordered[0];
                    var older = ordered.Skip(1).Select(m => m.File).ToList();
                    return new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.content.duplicate", ("name", keep.Name),
                            ("count", ordered.Count)),
                        [new CheckAction("keepNewest", new TextRef("pipeline.action.keepNewest"), async (c, job) =>
                        {
                            foreach (var file in older) { await _content.SetEnabled(c.Instance.Id, ContentType.Mod, file, false, job.Token); } }, true)], CanSilence: false);
                }).ToList();
            return Task.FromResult<IReadOnlyList<CheckResult>>(results.Count == 0 ? [Ok()] : results);
        }
    }
}
