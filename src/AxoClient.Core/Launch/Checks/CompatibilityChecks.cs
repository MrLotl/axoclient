using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;

namespace AxoClient.Core.Launch.Checks;

/// <summary>
/// Kompatibilität der Mods (nur nach Änderungen an der Instanz oder wenn "Mods bei jedem Start prüfen" an ist):
/// falscher Loader, falsche MC-Version, fehlende Abhängigkeiten, breaks/conflicts, Loader zu alt, Shader ohne Shader-Mod.
/// </summary>
public sealed class CompatibilityCheck(ContentManager content, ContentProviders providers) : ILaunchCheck
{
    /// <summary>IDs, die der Loader selbst mitbringt oder die über Fabric API laufen.</summary>
    private static readonly HashSet<string> Builtin = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "java", "fabricloader", "fabric-loader", "quilt_loader", "forge", "neoforge", "fml", "mixinextras"
    };

    public string Id => "compat";
    public LaunchPhase Phase => LaunchPhase.Compatibility;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        if (!ctx.ContentChanged && !ctx.Settings.Game.CheckModsOnLaunch)
            return [CheckResult.Ok(Id)];

        var results = new List<CheckResult>();
        var instance = ctx.Instance;
        var loader = instance.Loader.Type;
        var mc = instance.Minecraft.Version;
        var mods = ctx.Content.Mods.Where(m => m.Enabled && m.Source != ContentSource.AxoClient).ToList();
        var provided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.Where(m => m.Meta != null))
        {
            if (mod.Meta!.ModId != null)
                provided.Add(mod.Meta.ModId);
            provided.UnionWith(mod.Meta.Provides);
        }
        var installedProjects = ctx.Content.Mods.Where(m => m.ProjectId != null).Select(m => m.ProjectId!).ToHashSet();
        var hasFabricApi = provided.Contains("fabric-api") || provided.Contains("fabric");

        foreach (var mod in mods)
        {
            var meta = mod.Meta;
            var key = $"{mod.Meta?.ModId ?? mod.File}@{(mod.Hashes.Sha1 ?? "")[..Math.Min(8, mod.Hashes.Sha1?.Length ?? 0)]}";

            // Falscher Loader (Quilt lädt auch Fabric-Mods)
            if (meta is { Loaders.Count: > 0 } && loader != LoaderType.Vanilla && !LoaderFits(meta.Loaders, loader))
            {
                results.Add(new CheckResult(Id, Severity.Blocker, TextRef.Of("pipeline.compat.wrongLoader", ("name", mod.Name),
                        ("modLoader", LoaderLabel(meta.Loaders[0])), ("loader", LoaderNames.Display(loader))),
                    [ContentFilesCheck.DisableAction(content, ContentType.Mod, mod.File)], CanSilence: false));
                continue;
            }

            // Falsche MC-Version
            if (meta?.MinecraftRange is { } range && !VersionRange.Satisfies(mc, range))
            {
                var actions = new List<CheckAction>();
                if (mod.ProjectId != null && mod.Source is ContentSource.Modrinth or ContentSource.CurseForge)
                    actions.Add(new CheckAction("installMatching", new TextRef("pipeline.action.installMatching"),
                        (c, job) => content.InstallAsync(c.Instance.Id, ContentType.Mod,
                            new InstallRequest(mod.Source == ContentSource.CurseForge ? "curseforge" : "modrinth", mod.ProjectId!), job), true));
                actions.Add(ContentFilesCheck.DisableAction(content, ContentType.Mod, mod.File));
                results.Add(new CheckResult(Id, Severity.Warning, TextRef.Of("pipeline.compat.wrongVersion", ("name", mod.Name),
                    ("range", Text.Clean(range, 40)), ("version", mc)), actions, Key: $"version-mismatch:{key}"));
            }

            // Loader zu alt
            if (meta?.LoaderRange is { } loaderRange && instance.Loader.Version is { } loaderVersion
                                                      && !VersionRange.Satisfies(loaderVersion, loaderRange))
                results.Add(new CheckResult(Id, Severity.Warning, TextRef.Of("pipeline.compat.loaderTooOld", ("name", mod.Name),
                        ("loader", LoaderNames.Display(loader))),
                    [new CheckAction("updateLoader", new TextRef("pipeline.action.updateLoader"), UpdateLoaderAsync, true)],
                    Key: $"loader-old:{key}"));

            // Unverträglich (breaks) und Konflikte
            foreach (var other in meta?.Breaks.Where(provided.Contains) ?? [])
                results.Add(new CheckResult(Id, Severity.Blocker, TextRef.Of("pipeline.compat.breaks", ("name", mod.Name), ("other", NameOf(mods, other))),
                    [ContentFilesCheck.DisableAction(content, ContentType.Mod, mod.File)], CanSilence: false));
            foreach (var other in meta?.Conflicts.Where(provided.Contains) ?? [])
                results.Add(new CheckResult(Id, Severity.Warning, TextRef.Of("pipeline.compat.conflicts", ("name", mod.Name), ("other", NameOf(mods, other))),
                    [ContentFilesCheck.DisableAction(content, ContentType.Mod, mod.File)], Key: $"conflict:{key}:{other}"));

            // Fehlende Abhängigkeiten aus den Metadaten
            foreach (var dep in meta?.Depends ?? [])
            {
                if (Builtin.Contains(dep) || provided.Contains(dep) || (hasFabricApi && dep.StartsWith("fabric-", StringComparison.Ordinal)))
                    continue;
                results.Add(await MissingDependencyAsync(ctx, mod, dep, ct));
            }

            // Fehlende Abhängigkeiten laut Modrinth (Projekt-IDs)
            foreach (var projectId in mod.Dependencies.Where(p => !installedProjects.Contains(p)))
            {
                if (meta?.Depends.Count > 0)
                    break; // Metadaten sind genauer
                results.Add(InstallDependency(mod, projectId, mod.Source == ContentSource.CurseForge ? "curseforge" : "modrinth", projectId));
            }
        }

        // Shader ohne Shader-Mod
        if (ctx.Content.Shaderpacks.Any(s => s.Enabled) && !provided.Overlaps(["iris", "oculus", "optifine", "optifabric"]))
        {
            var shaderMod = loader is LoaderType.Fabric or LoaderType.Quilt or LoaderType.NeoForge ? "iris"
                : loader == LoaderType.Forge ? "oculus" : null;
            results.Add(new CheckResult(Id, Severity.Warning, new TextRef("pipeline.compat.shaderWithoutMod"),
                shaderMod == null
                    ? []
                    : [new CheckAction("installIris", TextRef.Of("pipeline.action.installShaderMod", ("name", shaderMod == "iris" ? "Iris" : "Oculus")),
                        (c, job) => content.InstallAsync(c.Instance.Id, ContentType.Mod, new InstallRequest("modrinth", shaderMod), job), true)],
                Key: "shader-without-mod"));
        }

        return results.Count == 0 ? [CheckResult.Ok(Id)] : results.DistinctBy(r => r.Message.ToString()).ToList();
    }

    private async Task<CheckResult> MissingDependencyAsync(LaunchContext ctx, ContentEntry mod, string dependencyId, CancellationToken ct)
    {
        // Viele Mod-IDs entsprechen dem Modrinth-Slug: dort nach einer passenden Version suchen
        try
        {
            if (Text.IsValidId(dependencyId.Replace('_', '-')))
            {
                var modrinth = providers.Get("modrinth");
                var versions = await modrinth.GetVersionsAsync(dependencyId, ctx.Instance.Minecraft.Version, ctx.Instance.Loader.Type,
                    ProjectKind.Mod, ct);
                if (versions.Count > 0)
                    return InstallDependency(mod, dependencyId, "modrinth", versions[0].ProjectId);
            }
        }
        catch (Exception ex) when (ex is AxoException or HttpRequestException)
        {
        }
        return new CheckResult(Id, Severity.Blocker, TextRef.Of("pipeline.compat.dependencyUnavailable", ("name", mod.Name), ("dependency", dependencyId)),
            [ContentFilesCheck.DisableAction(content, ContentType.Mod, mod.File)], CanSilence: false);
    }

    private CheckResult InstallDependency(ContentEntry mod, string label, string source, string projectId) =>
        new(Id, Severity.AutoFix, TextRef.Of("pipeline.compat.dependencyInstall", ("name", mod.Name), ("dependency", label)),
            [new CheckAction("installDependency", new TextRef("pipeline.action.install"),
                (c, job) => content.InstallAsync(c.Instance.Id, ContentType.Mod, new InstallRequest(source, projectId), job), true)]);

    private static async Task UpdateLoaderAsync(LaunchContext ctx, JobContext job)
    {
        // Leere Version = beim nächsten Schritt die empfohlene eintragen
        ctx.Instance.Loader = ctx.Instance.Loader with { Version = null };
        ctx.VersionName = null;
        await Task.CompletedTask;
    }

    private static bool LoaderFits(List<string> modLoaders, LoaderType loader) => loader switch
    {
        LoaderType.Quilt => modLoaders.Any(l => l is "quilt" or "fabric"),
        LoaderType.Fabric => modLoaders.Contains("fabric"),
        LoaderType.Forge => modLoaders.Contains("forge"),
        LoaderType.NeoForge => modLoaders.Contains("neoforge"),
        _ => true
    };

    private static string LoaderLabel(string id) => id switch
    {
        "neoforge" => "NeoForge",
        "fabric" => "Fabric",
        "forge" => "Forge",
        "quilt" => "Quilt",
        _ => id
    };

    private static string NameOf(List<ContentEntry> mods, string modId) =>
        mods.FirstOrDefault(m => string.Equals(m.Meta?.ModId, modId, StringComparison.OrdinalIgnoreCase))?.Name ?? modId;
}
