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
        // Was deaktivierte Mods bereitstellen würden: fehlt so eine Abhängigkeit, nicht still neu installieren
        var disabledProviders = new Dictionary<string, ContentEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in ctx.Content.Mods.Where(m => !m.Enabled && m.Meta != null && m.Source != ContentSource.AxoClient))
            foreach (var id in mod.Meta!.ModId is { } own ? mod.Meta.Provides.Prepend(own) : mod.Meta.Provides)
                disabledProviders.TryAdd(id, mod);
        var hasFabricApi = provided.Contains("fabric-api") || provided.Contains("fabric");
        // Fehlende Abhängigkeiten erst sammeln und dann gemeinsam bei Modrinth nachschlagen (jede nur einmal)
        var missing = new List<(ContentEntry Mod, string Dependency)>();

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

            // Loader zu alt (Forge-Versionen tauchen auch als "1.20.1-47.2.0" auf)
            if (meta?.LoaderRange is { } loaderRange && instance.Loader.Version is { } loaderVersion
                                                      && !VersionRange.Satisfies(StripMinecraftPrefix(loaderVersion, mc), loaderRange))
                results.Add(new CheckResult(Id, Severity.Warning, TextRef.Of("pipeline.compat.loaderTooOld", ("name", mod.Name),
                        ("loader", LoaderNames.Display(loader))),
                    [new CheckAction("updateLoader", new TextRef("pipeline.action.updateLoader"), UpdateLoaderAsync, true)],
                    Key: $"loader-old:{key}"));

            // Unverträglich (breaks) und Konflikte – nur, wenn die installierte Version unter die Bedingung fällt
            foreach (var other in meta?.Breaks.Where(id => Hits(mods, provided, id, meta.BreakRanges, meta.Format)) ?? [])
                results.Add(new CheckResult(Id, Severity.Blocker, TextRef.Of("pipeline.compat.breaks", ("name", mod.Name), ("other", NameOf(mods, other))),
                    [ContentFilesCheck.DisableAction(content, ContentType.Mod, mod.File)], CanSilence: false));
            foreach (var other in meta?.Conflicts.Where(id => Hits(mods, provided, id, meta.ConflictRanges, meta.Format)) ?? [])
                results.Add(new CheckResult(Id, Severity.Warning, TextRef.Of("pipeline.compat.conflicts", ("name", mod.Name), ("other", NameOf(mods, other))),
                    [ContentFilesCheck.DisableAction(content, ContentType.Mod, mod.File)], Key: $"conflict:{key}:{other}"));

            // Fehlende Abhängigkeiten aus den Metadaten
            foreach (var dep in meta?.Depends ?? [])
            {
                if (Builtin.Contains(dep) || provided.Contains(dep) || (hasFabricApi && dep.StartsWith("fabric-", StringComparison.Ordinal)))
                    continue;
                if (disabledProviders.TryGetValue(dep, out var off))
                {
                    // Bewusst deaktiviert: nichts nachinstallieren, sondern fragen – zuerst die Mod, die es braucht, abschalten
                    results.Add(new CheckResult(Id, Severity.Blocker,
                        TextRef.Of("pipeline.compat.dependencyDisabled", ("name", mod.Name), ("dependency", off.Name)),
                        [
                            ContentFilesCheck.DisableAction(content, ContentType.Mod, mod.File),
                            new CheckAction("enableDependency", TextRef.Of("pipeline.action.enableMod", ("name", off.Name)),
                                (c, job) => content.SetEnabledAsync(c.Instance.Id, ContentType.Mod, off.File, true, job.Token))
                        ], CanSilence: false));
                    continue;
                }
                missing.Add((mod, dep));
            }

            // Fehlende Abhängigkeiten laut Modrinth (Projekt-IDs) – nur, wenn die Jar selbst nichts verrät. Die Angaben
            // auf Modrinth gelten oft für eine andere Loader-Variante oder sind schon in der Jar enthalten.
            if (meta == null)
                foreach (var projectId in mod.Dependencies.Where(p => !installedProjects.Contains(p)))
                    results.Add(InstallDependency(mod, projectId, mod.Source == ContentSource.CurseForge ? "curseforge" : "modrinth", projectId));
        }

        if (missing.Count > 0)
        {
            var found = await LookupDependenciesAsync(ctx, missing.Select(m => m.Dependency).Distinct(StringComparer.OrdinalIgnoreCase), ct);
            foreach (var (mod, dep) in missing)
                results.Add(found.GetValueOrDefault(dep) is { } projectId
                    ? InstallDependency(mod, dep, "modrinth", projectId)
                    // Warnung statt Blocker: die Erkennung kann sich irren (z. B. ungewöhnlich verpackte Mods), dann soll
                    // man trotzdem starten oder die Meldung stummschalten können
                    : new CheckResult(Id, Severity.Warning, TextRef.Of("pipeline.compat.dependencyUnavailable", ("name", mod.Name), ("dependency", dep)),
                        [ContentFilesCheck.DisableAction(content, ContentType.Mod, mod.File)], Key: $"dependency-missing:{mod.File}:{dep}"));
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

    /// <summary>
    /// Sucht fehlende Abhängigkeiten bei Modrinth (viele Mod-IDs entsprechen dem Slug): parallel, jede ID nur einmal und
    /// mit kurzem Zeitlimit. Sonst dauert die Prüfung bei großen Packs oder gedrosseltem Modrinth Minuten und der Start
    /// scheint bei 60 % zu hängen. Ergebnis: Mod-ID → Projekt-ID (fehlt = nicht gefunden).
    /// </summary>
    private async Task<Dictionary<string, string>> LookupDependenciesAsync(LaunchContext ctx, IEnumerable<string> dependencyIds,
        CancellationToken ct)
    {
        var modrinth = providers.Get("modrinth");
        var found = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(dependencyIds.Where(d => Text.IsValidId(d.Replace('_', '-'))).Select(async dependencyId =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var versions = await modrinth.GetVersionsAsync(dependencyId, ctx.Instance.Minecraft.Version, ctx.Instance.Loader.Type,
                    ProjectKind.Mod, timeout.Token);
                if (versions.Count > 0)
                    found[dependencyId] = versions[0].ProjectId;
            }
            catch (Exception ex) when (ex is AxoException or HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                // Nicht gefunden, nicht erreichbar oder zu langsam: gilt als nicht verfügbar
            }
            finally
            {
                gate.Release();
            }
        }));
        return new Dictionary<string, string>(found, StringComparer.OrdinalIgnoreCase);
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

    private static string StripMinecraftPrefix(string loaderVersion, string mc) =>
        loaderVersion.StartsWith(mc + "-", StringComparison.Ordinal) ? loaderVersion[(mc.Length + 1)..] : loaderVersion;

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

    /// <summary>
    /// Ob eine breaks/conflicts-Angabe auf eine installierte Mod zutrifft. Ohne Bedingung zählt jede Version; mit
    /// Bedingung nur eine bekannte Version, die sie erfüllt. Ältere Metadaten ohne Bedingungen zählen nie, sonst
    /// würde z. B. „Sodium bricht Iris &lt; 1.8“ jede Iris-Version sperren.
    /// </summary>
    private static bool Hits(List<ContentEntry> mods, HashSet<string> provided, string modId, Dictionary<string, string> ranges, int format)
    {
        if (format < ModMetadata.CurrentFormat || !provided.Contains(modId))
            return false;
        if (!ranges.TryGetValue(modId, out var range) || range.Trim() is "*" or "")
            return true;
        var versions = mods.Where(m => string.Equals(m.Meta?.ModId, modId, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Meta!.ModVersion).OfType<string>().ToList();
        return versions.Any(v => VersionRange.Satisfies(v, range, unknown: false));
    }

    private static string NameOf(List<ContentEntry> mods, string modId) =>
        mods.FirstOrDefault(m => string.Equals(m.Meta?.ModId, modId, StringComparison.OrdinalIgnoreCase))?.Name ?? modId;
}
