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

        public override async Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken)
        {
            var custom = context.Settings.Defaults.Java;
            if (custom == "auto")
            {
                context.JavaOverride = null;
                return [Ok()];
            }
            context.JavaOverride = custom;
            var required = _java.RequiredMajor(context.VersionName ?? context.Instance.Minecraft.Version);
            var major = File.Exists(custom) ? await _java.ReadMajor(custom, cancellationToken) : null;
            if (major is { } majorVersion && _java.IsCompatible(majorVersion, required))
            {
                return [Ok()];
            }

            return [new CheckResult(Id, Severity.Warning,
                major == null
                    ? new TextRef("pipeline.java.notFound", ("path", Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(custom))) ?? custom))
                    : new TextRef("pipeline.java.mismatch", ("have", major), ("need", required)),
                [
                    new CheckAction("auto", new TextRef("pipeline.action.javaAuto"), async (launchContext, _) =>
                    {
                        launchContext.JavaOverride = null;
                        await _settings.Update(settings => settings.Defaults.Java = "auto");
                    }, true)
                ], Key: $"java-mismatch:{required}")];
        }
    }
}
