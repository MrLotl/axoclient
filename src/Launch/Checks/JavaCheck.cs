using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.Game.Contract;
using AxoClient.Settings.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>Eigener Java-Pfad passt nicht zur MC-Version → Warnung "Automatisch wählen lassen".</summary>
    public sealed class JavaCheck : LaunchCheck
    {
        private readonly IJavaManager _java;
        private readonly ISettingsService _settings;

        public JavaCheck(IJavaManager java, ISettingsService settings)
        {
            _java = java;
            _settings = settings;
        }

        public override string Id => "java";
        public override LaunchPhase Phase => LaunchPhase.GameFiles;

        public override async Task<IReadOnlyList<CheckResult>> Run(LaunchContext ctx, CancellationToken ct)
        {
            var custom = ctx.Settings.Defaults.Java;
            if (custom == "auto")
            {
                ctx.JavaOverride = null;
                return [Ok()];
            }
            ctx.JavaOverride = custom;
            var required = _java.RequiredMajor(ctx.VersionName ?? ctx.Instance.Minecraft.Version);
            var major = File.Exists(custom) ? await _java.ReadMajor(custom, ct) : null;
            if (major is { } m && _java.IsCompatible(m, required))
            {
                return [Ok()];
            }

            return [new CheckResult(Id, Severity.Warning,
                major == null
                    ? new TextRef("pipeline.java.notFound", ("path", Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(custom))) ?? custom))
                    : new TextRef("pipeline.java.mismatch", ("have", major), ("need", required)),
                [
                    new CheckAction("auto", new TextRef("pipeline.action.javaAuto"), async (c, _) =>
                    {
                        c.JavaOverride = null;
                        await _settings.Update(s => s.Defaults.Java = "auto");
                    }, true)
                ], Key: $"java-mismatch:{required}")];
        }
    }
}
