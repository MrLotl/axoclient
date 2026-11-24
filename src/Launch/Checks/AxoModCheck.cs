using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Launch;
using AxoClient.Game.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>Legt die passende AxoClient-Mod in die Instanz oder entfernt sie (Auto-Fix bzw. Hinweis ohne Dialog).</summary>
    public sealed class AxoModCheck : LaunchCheck
    {
        private readonly IAxoModInstaller _axoMod;
        private readonly IContentManager _content;

        public AxoModCheck(IAxoModInstaller axoMod, IContentManager content)
        {
            _axoMod = axoMod;
            _content = content;
        }

        public override string Id => "axomod";
        public override LaunchPhase Phase => LaunchPhase.Content;

        public override Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken)
        {
            var instance = context.Instance;
            var supported = _axoMod.IsSupported(instance.Minecraft.Version, instance.Loader.Type);
            if (!supported && instance.AxoMod)
            {
                context.Hints.Add(new TextRef("pipeline.axomod.unsupported"));
            }

            var wanted = supported && instance.AxoMod ? _axoMod.Find(instance.Minecraft.Version, instance.Loader.Type) : null;
            var current = context.Content.Mods.FirstOrDefault(mod => mod.Source == ContentSource.AxoClient);
            var currentFile = current == null ? null : Path.Combine(context.GameDir, "mods", current.File);
            var upToDate = wanted == null
                ? current == null
                : current?.File == wanted.File && currentFile != null && _axoMod.IsCurrent(wanted, currentFile);
            var results = new List<CheckResult>();
            if (!upToDate)
            {
                results.Add(new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.axomod.sync"),
                    [new CheckAction("sync", new TextRef("pipeline.action.install"), (launchContext, job) => sync(launchContext, job), true)]));
            }
            // Die Badge-Mod des alten Launchers (aus importierten Instanzen) würde neben der AxoMod doppelt laufen
            var legacy = wanted == null ? [] : context.Content.Mods.Where(mod => mod.Enabled && isLegacyBadgeMod(mod)).Select(mod => mod.File).ToList();
            if (legacy.Count > 0)
            {
                results.Add(new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.axomod.legacy"),
                    [new CheckAction("disableLegacy", new TextRef("pipeline.action.disableMod"), (launchContext, job) => disable(launchContext, legacy, job), true)]));
            }

            return Task.FromResult<IReadOnlyList<CheckResult>>(results.Count == 0 ? [Ok()] : results);
        }

        private bool isLegacyBadgeMod(ContentEntry mod) =>
            mod.Source != ContentSource.AxoClient
            && (mod.File.StartsWith("mclauncher-badge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mod.Meta?.ModId, "mclauncher_badge", StringComparison.OrdinalIgnoreCase));

        /// <summary>Deaktiviert statt löscht (umkehrbar); am Mod-Schutz vorbei, weil die Datei nie zum Pack gehört.</summary>
        private Task disable(LaunchContext context, IReadOnlyList<string> files, JobContext job) =>
            _content.Change(context.Instance.Id, document =>
            {
                var directory = Path.Combine(context.GameDir, "mods");
                foreach (var mod in document.Mods.Where(mod => mod.Enabled && files.Contains(mod.File, StringComparer.OrdinalIgnoreCase)))
                {
                    var from = Path.Combine(directory, mod.DiskName);
                    mod.Enabled = false;
                    if (File.Exists(from))
                    {
                        File.Move(from, Path.Combine(directory, mod.DiskName), overwrite: true);
                    }
                }
            }, job.Token);

        private async Task sync(LaunchContext context, JobContext job)
        {
            var entry = await _axoMod.Sync(Path.Combine(context.GameDir, "mods"), context.Instance.Minecraft.Version,
                context.Instance.Loader.Type, context.Instance.AxoMod, job.Token);
            await _content.Change(context.Instance.Id, document =>
            {
                document.Mods.RemoveAll(mod => mod.Source == ContentSource.AxoClient);
                if (entry != null)
                {
                    document.Mods.Add(entry);
                }
            }, job.Token);
        }
    }
}
