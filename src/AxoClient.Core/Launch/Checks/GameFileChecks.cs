using AxoClient.Core.Common;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Launch.Checks;

/// <summary>
/// Version und Loader: fehlt etwas oder ist beschädigt → Auto-Fix (neu laden/installieren). CmlLib prüft dabei
/// alle Dateien gegen ihre Hashes; vollständige Installationen sind in Sekunden durch.
/// </summary>
public sealed class GameFilesCheck(GameInstaller installer, LoaderManager loaders, InstanceManager instances) : ILaunchCheck
{
    public string Id => "gameFiles";
    public LaunchPhase Phase => LaunchPhase.GameFiles;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        if (ctx.Offline)
            return ctx.VersionName != null ? [CheckResult.Ok(Id)] : [Blocker()];

        var instance = ctx.Instance;
        if (instance.Loader.Type != LoaderType.Vanilla && instance.Loader.Version == null)
        {
            // Loader-Version noch offen: empfohlene eintragen (eindeutig und ungefährlich)
            var version = await loaders.ResolveVersionAsync(instance.Loader.Type, instance.Minecraft.Version, null, ct);
            instance.Loader = instance.Loader with { Version = version };
            await instances.SaveAsync(instance, ct);
        }

        var installed = installer.InstalledVersionName(instance.Minecraft.Version, instance.Loader.Type, instance.Loader.Version);
        ctx.VersionName = installed;
        ctx.NeedsInstall = installed == null || !installer.IsVersionPresent(installed);
        return [new CheckResult(Id, Severity.AutoFix,
            new TextRef(ctx.NeedsInstall ? "pipeline.gameFiles.install" : "pipeline.gameFiles.verify"),
            [new CheckAction("install", new TextRef("pipeline.action.install"), InstallAsync, true)])];
    }

    private async Task InstallAsync(LaunchContext ctx, JobContext job) =>
        ctx.VersionName = await installer.InstallAsync(ctx.GamePath, ctx.Instance.Minecraft.Version, ctx.Instance.Loader.Type,
            ctx.Instance.Loader.Version, job);

    private CheckResult Blocker() => new(Id, Severity.Blocker, new TextRef("pipeline.internet.missingFiles"),
        [new CheckAction("retry", new TextRef("pipeline.action.retry"), (c, _) =>
        {
            c.Offline = false;
            return Task.CompletedTask;
        }, true)], CanSilence: false);
}

/// <summary>Eigener Java-Pfad passt nicht zur MC-Version → Warnung "Automatisch wählen lassen".</summary>
public sealed class JavaCheck(JavaManager java, SettingsService settings) : ILaunchCheck
{
    public string Id => "java";
    public LaunchPhase Phase => LaunchPhase.GameFiles;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        var custom = ctx.Settings.Defaults.Java;
        if (custom == "auto")
        {
            ctx.JavaOverride = null;
            return [CheckResult.Ok(Id)];
        }
        ctx.JavaOverride = custom;
        var required = java.RequiredMajor(ctx.VersionName ?? ctx.Instance.Minecraft.Version);
        var major = File.Exists(custom) ? await JavaManager.ReadMajorAsync(custom, ct) : null;
        if (major is { } m && JavaManager.IsCompatible(m, required))
            return [CheckResult.Ok(Id)];
        return [new CheckResult(Id, Severity.Warning,
            major == null
                ? TextRef.Of("pipeline.java.notFound", ("path", Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(custom))) ?? custom))
                : TextRef.Of("pipeline.java.mismatch", ("have", major), ("need", required)),
            [
                new CheckAction("auto", new TextRef("pipeline.action.javaAuto"), async (c, _) =>
                {
                    c.JavaOverride = null;
                    await settings.UpdateAsync(s => s.Defaults.Java = "auto");
                }, true)
            ], Key: $"java-mismatch:{required}")];
    }
}
