using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Instances;
using AxoClient.DataModel.Launch;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;

namespace AxoClient.Launch.Checks
{
    /// <summary>
    /// Kompatibilität der Mods (nur nach Änderungen an der Instanz oder wenn "Mods bei jedem Start prüfen" an ist):
    /// falscher Loader, falsche MC-Version, fehlende Abhängigkeiten, breaks/conflicts, Loader zu alt, Shader ohne Shader-Mod.
    /// </summary>
    public sealed class CompatibilityCheck : LaunchCheck
    {
        private readonly IContentManager _content;
        private readonly IContentProviders _providers;
        private readonly ITextHelper _textHelper;
        private readonly IAppLog _appLog;
        private readonly ILoaderNames _loaderNames;
        private readonly IVersionRange _versionRange;
        private readonly IModMetadataReader _modMetadataReader;
        private readonly IModCompatibility _modCompatibility;
        private readonly ILoaderManager? _loaders;
        private readonly IInstanceManager? _instances;
        /// <summary>IDs, die der Loader selbst mitbringt oder die über Fabric API laufen.</summary>
        private readonly HashSet<string> _builtin = new(StringComparer.OrdinalIgnoreCase)
        {
            "minecraft", "java", "fabricloader", "fabric-loader", "quilt_loader", "forge", "neoforge", "fml", "mixinextras"
        };

        public CompatibilityCheck(
            IContentManager content,
            IContentProviders providers,
            ITextHelper textHelper,
            IAppLog appLog,
            ILoaderNames loaderNames,
            IVersionRange versionRange,
            IModMetadataReader modMetadataReader,
            IModCompatibility modCompatibility,
            ILoaderManager? loaders = null,
            IInstanceManager? instances = null)
        {
            _content = content;
            _providers = providers;
            _textHelper = textHelper;
            _appLog = appLog;
            _loaderNames = loaderNames;
            _versionRange = versionRange;
            _modMetadataReader = modMetadataReader;
            _modCompatibility = modCompatibility;
            _loaders = loaders;
            _instances = instances;
        }

        public override string Id => "compat";
        public override LaunchPhase Phase => LaunchPhase.Compatibility;

        public override async Task<IReadOnlyList<CheckResult>> Run(LaunchContext ctx, CancellationToken ct)
        {
            if (!ctx.ContentChanged && !ctx.Settings.Game.CheckModsOnLaunch)
            {
                return [Ok()];
            }

            var results = new List<CheckResult>();
            var instance = ctx.Instance;
            var loader = instance.Loader.Type;
            var mc = instance.Minecraft.Version;
            useLoaderVariants(ctx, loader, mc);
            var mods = ctx.Content.Mods.Where(m => m.Enabled && m.Source != ContentSource.AxoClient).ToList();
            var provided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in mods.Where(m => m.Meta != null))
            {
                if (mod.Meta!.ModId != null)
                {
                    provided.Add(mod.Meta.ModId);
                }

                provided.UnionWith(mod.Meta.Provides);
            }
            var installedProjects = ctx.Content.Mods.Where(m => m.ProjectId != null).Select(m => m.ProjectId!).ToHashSet();
            // Was deaktivierte Mods bereitstellen würden: fehlt so eine Abhängigkeit, nicht still neu installieren
            var disabledProviders = new Dictionary<string, ContentEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in ctx.Content.Mods.Where(m => !m.Enabled && m.Meta != null && m.Source != ContentSource.AxoClient))
            {
                foreach (var id in mod.Meta!.ModId is { } own ? mod.Meta.Provides.Prepend(own) : mod.Meta.Provides)
                {
                    disabledProviders.TryAdd(id, mod);
                }
            }

            var hasFabricApi = provided.Contains("fabric-api") || provided.Contains("fabric");
            // Fehlende Abhängigkeiten erst sammeln und dann gemeinsam bei Modrinth nachschlagen (jede nur einmal)
            var missing = new List<(ContentEntry Mod, string Dependency)>();

            foreach (var mod in mods)
            {
                var meta = mod.Meta;
                var key = $"{mod.Meta?.ModId ?? mod.File}@{(mod.Hashes.Sha1 ?? "")[..Math.Min(8, mod.Hashes.Sha1?.Length ?? 0)]}";

                // Falscher Loader (Quilt lädt auch Fabric-Mods)
                if (meta is { Loaders.Count: > 0 } && loader != LoaderType.Vanilla && !_modCompatibility.LoaderFits(meta.Loaders, loader, mc))
                {
                    results.Add(new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.compat.wrongLoader", ("name", mod.Name),
                            ("modLoader", loaderLabel(meta.Loaders[0])), ("loader", _loaderNames.Display(loader))),
                        [DisableAction(_content, ContentType.Mod, mod.File)], CanSilence: false));
                    continue;
                }

                // Falsche MC-Version
                if (meta?.MinecraftRange is { } range && !_versionRange.SatisfiesLoosely(mc, range))
                {
                    var actions = new List<CheckAction>();
                    if (mod.ProjectId != null && mod.Source is ContentSource.Modrinth or ContentSource.CurseForge)
                    {
                        actions.Add(new CheckAction("installMatching", new TextRef("pipeline.action.installMatching"),
                            (c, job) => _content.Install(c.Instance.Id, ContentType.Mod,
                                new InstallRequest(mod.Source == ContentSource.CurseForge ? "curseforge" : "modrinth", mod.ProjectId!), job), true));
                    }

                    actions.Add(DisableAction(_content, ContentType.Mod, mod.File));
                    results.Add(new CheckResult(Id, Severity.Warning, new TextRef("pipeline.compat.wrongVersion", ("name", mod.Name),
                        ("range", _textHelper.Clean(range, 40)), ("version", mc)), actions, Key: $"version-mismatch:{key}"));
                }

                // Loader zu alt (Forge-Versionen tauchen auch als "1.20.1-47.2.0" auf)
                if (meta?.LoaderRange is { } loaderRange && instance.Loader.Version is { } loaderVersion
                                                          && !_versionRange.Satisfies(stripMinecraftPrefix(loaderVersion, mc), loaderRange))
                {
                    results.Add(new CheckResult(Id, Severity.Warning, new TextRef("pipeline.compat.loaderTooOld", ("name", mod.Name),
                            ("loader", _loaderNames.Display(loader))),
                        [new CheckAction("updateLoader", new TextRef("pipeline.action.updateLoader"), updateLoader, true)],
                        Key: $"loader-old:{key}"));
                }

                // Unverträglich (breaks) und Konflikte – nur, wenn die installierte Version unter die Bedingung fällt
                foreach (var other in meta?.Breaks.Where(id => hits(mods, provided, id, meta.BreakRanges, meta.Format)) ?? [])
                {
                    results.Add(new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.compat.breaks", ("name", mod.Name), ("other", nameOf(mods, other))),
                        [DisableAction(_content, ContentType.Mod, mod.File)], CanSilence: false));
                }

                foreach (var other in meta?.Conflicts.Where(id => hits(mods, provided, id, meta.ConflictRanges, meta.Format)) ?? [])
                {
                    results.Add(new CheckResult(Id, Severity.Warning, new TextRef("pipeline.compat.conflicts", ("name", mod.Name), ("other", nameOf(mods, other))),
                        [DisableAction(_content, ContentType.Mod, mod.File)], Key: $"conflict:{key}:{other}"));
                }

                // Fehlende Abhängigkeiten aus den Metadaten
                foreach (var dep in meta?.Depends ?? [])
                {
                    if (_builtin.Contains(dep) || provided.Contains(dep) || (hasFabricApi && dep.StartsWith("fabric-", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    if (disabledProviders.TryGetValue(dep, out var off))
                    {
                        // Deaktiviert: nichts nachinstallieren. Vorschlag: die Abhängigkeit wieder aktivieren, sonst die Mod abschalten
                        results.Add(new CheckResult(Id, Severity.Blocker,
                            new TextRef("pipeline.compat.dependencyDisabled", ("name", mod.Name), ("dependency", off.Name)),
                            [
                                new CheckAction("enableDependency", new TextRef("pipeline.action.enableMod", ("name", off.Name)),
                                    (c, job) => _content.SetEnabled(c.Instance.Id, ContentType.Mod, off.File, true, job.Token), true),
                                DisableAction(_content, ContentType.Mod, mod.File)
                            ], CanSilence: false));
                        continue;
                    }
                    missing.Add((mod, dep));
                }

                // Fehlende Abhängigkeiten laut Modrinth (Projekt-IDs) – nur, wenn die Jar selbst nichts verrät. Die Angaben
                // auf Modrinth gelten oft für eine andere Loader-Variante oder sind schon in der Jar enthalten.
                if (meta == null)
                {
                    foreach (var projectId in mod.Dependencies.Where(p => !installedProjects.Contains(p)))
                    {
                        results.Add(installDependency(mod, projectId, mod.Source == ContentSource.CurseForge ? "curseforge" : "modrinth", projectId));
                    }
                }
            }

            if (missing.Count > 0)
            {
                var found = await lookupDependencies(ctx, missing.Select(m => m.Dependency).Distinct(StringComparer.OrdinalIgnoreCase), ct);
                foreach (var (mod, dep) in missing)
                {
                    results.Add(found.GetValueOrDefault(dep) is { } projectId
                        ? installDependency(mod, dep, "modrinth", projectId)
                        // Warnung statt Blocker: die Erkennung kann sich irren (z. B. ungewöhnlich verpackte Mods), dann soll
                        // man trotzdem starten oder die Meldung stummschalten können
                        : new CheckResult(Id, Severity.Warning, new TextRef("pipeline.compat.dependencyUnavailable", ("name", mod.Name), ("dependency", dep)),
                            [DisableAction(_content, ContentType.Mod, mod.File)], Key: $"dependency-missing:{mod.File}:{dep}"));
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
                        : [new CheckAction("installIris", new TextRef("pipeline.action.installShaderMod", ("name", shaderMod == "iris" ? "Iris" : "Oculus")),
                            (c, job) => _content.Install(c.Instance.Id, ContentType.Mod, new InstallRequest("modrinth", shaderMod), job), true)],
                    Key: "shader-without-mod"));
            }

            return results.Count == 0 ? [Ok()] : results.DistinctBy(r => r.Message.ToString()).ToList();
        }

        /// <summary>
        /// Sucht fehlende Abhängigkeiten bei Modrinth (viele Mod-IDs entsprechen dem Slug): parallel, jede ID nur einmal und
        /// mit kurzem Zeitlimit. Sonst dauert die Prüfung bei großen Packs oder gedrosseltem Modrinth Minuten und der Start
        /// scheint bei 60 % zu hängen. Ergebnis: Mod-ID → Projekt-ID (fehlt = nicht gefunden).
        /// </summary>
        private async Task<Dictionary<string, string>> lookupDependencies(LaunchContext ctx, IEnumerable<string> dependencyIds,
            CancellationToken ct)
        {
            var modrinth = _providers.Get("modrinth");
            var found = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var gate = new SemaphoreSlim(6);
            await Task.WhenAll(dependencyIds.Where(d => _textHelper.IsValidId(d.Replace('_', '-'))).Select(async dependencyId =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    var versions = await modrinth.GetVersions(dependencyId, ctx.Instance.Minecraft.Version, ctx.Instance.Loader.Type,
                        ProjectKind.Mod, timeout.Token);
                    if (versions.Count > 0)
                    {
                        found[dependencyId] = versions[0].ProjectId;
                    }
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

        /// <summary>
        /// Fehlende Abhängigkeit: fragen statt still installieren. Die Erkennung kann sich irren (dann würde eine fremde Mod
        /// installiert), und eine stille Installation lief beim Start ohne eigene Anzeige – der Start schien bei 60 % zu hängen.
        /// </summary>
        private CheckResult installDependency(ContentEntry mod, string label, string source, string projectId) =>
            new(Id, Severity.Warning, new TextRef("pipeline.compat.dependencyMissing", ("name", mod.Name), ("dependency", label)),
                [
                    new CheckAction("installDependency", new TextRef("pipeline.action.installNamed", ("name", label)),
                        (c, job) => _content.Install(c.Instance.Id, ContentType.Mod, new InstallRequest(source, projectId), job), true),
                    DisableAction(_content, ContentType.Mod, mod.File)
                ], Key: $"dependency-missing:{mod.File}:{label}");

        /// <summary>
        /// Neueste Loader-Version, die alle Mods zufriedenstellt, eintragen und speichern. Vorher wurde nur im Speicher
        /// "leer" gesetzt: beim Reparieren passierte nichts, und beim Start kam die empfohlene – oft wieder zu alte – Version.
        /// </summary>
        private async Task updateLoader(LaunchContext ctx, JobContext job)
        {
            var loader = ctx.Instance.Loader.Type;
            var mc = ctx.Instance.Minecraft.Version;
            if (_loaders == null || _instances == null)
            {
                ctx.Instance.Loader = ctx.Instance.Loader with { Version = null };
                ctx.VersionName = null;
                return;
            }
            job.Status("pipeline.compat.loaderSearching");
            var ranges = ctx.Content.Mods.Where(m => m.Enabled && m.Meta?.LoaderRange is { Length: > 0 }).Select(m => m.Meta!.LoaderRange!).ToList();
            var versions = (await _loaders.ListVersions(loader, mc, job.Token))
                .OrderByDescending(v => stripMinecraftPrefix(v.Version, mc), Comparer<string>.Create(_versionRange.Compare)).ToList();
            bool Fits(LoaderVersionInfo v) => ranges.All(r => _versionRange.Satisfies(stripMinecraftPrefix(v.Version, mc), r));
            var pick = versions.FirstOrDefault(v => v.Stable && Fits(v)) ?? versions.FirstOrDefault(Fits)
                       ?? throw new AxoException("pipeline.compat.noLoaderVersion", new() { ["loader"] = _loaderNames.Display(loader) });
            _appLog.Info($"Loader für {ctx.Instance.Id}: {ctx.Instance.Loader.Version} → {pick.Version}");
            ctx.Instance = await _instances.Update(ctx.Instance.Id, new InstanceUpdate(Loader: loader, LoaderVersion: pick.Version),
                job.Token);
            // Neue Version muss vor dem Start installiert werden
            ctx.VersionName = null;
        }

        /// <summary>
        /// Jars mit Metadaten für mehrere Loader (z. B. Collective: Fabric und NeoForge) wurden evtl. mit den Angaben eines
        /// anderen Loaders eingelesen. Für die Prüfung die Angaben zum Loader der Instanz nehmen (nur im Speicher).
        /// </summary>
        private void useLoaderVariants(LaunchContext ctx, LoaderType loader, string mc)
        {
            if (loader == LoaderType.Vanilla)
            {
                return;
            }

            foreach (var mod in ctx.Content.Mods)
            {
                if (mod.Meta is not { Loaders.Count: > 1 } meta || _modCompatibility.LoaderFits([meta.Loaders[0]], loader, mc) || !_modCompatibility.LoaderFits(meta.Loaders, loader, mc))
                {
                    continue;
                }

                var path = Path.Combine(ctx.GameDir, "mods", mod.DiskName);
                if (File.Exists(path) && _modMetadataReader.Read(path, _loaderNames.ModrinthId(loader))?.Meta is { } variant)
                {
                    mod.Meta = variant;
                }
            }
        }

        private string stripMinecraftPrefix(string loaderVersion, string mc) =>
            loaderVersion.StartsWith(mc + "-", StringComparison.Ordinal) ? loaderVersion[(mc.Length + 1)..] : loaderVersion;

        private string loaderLabel(string id) => id switch
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
        private bool hits(List<ContentEntry> mods, HashSet<string> provided, string modId, Dictionary<string, string> ranges, int format)
        {
            if (format < ModMetadata.CurrentFormat || !provided.Contains(modId))
            {
                return false;
            }

            if (!ranges.TryGetValue(modId, out var range) || range.Trim() is "*" or "")
            {
                return true;
            }

            var versions = mods.Where(m => string.Equals(m.Meta?.ModId, modId, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Meta!.ModVersion).OfType<string>().ToList();
            return versions.Any(v => _versionRange.Satisfies(v, range, unknown: false));
        }

        private string nameOf(List<ContentEntry> mods, string modId) =>
            mods.FirstOrDefault(m => string.Equals(m.Meta?.ModId, modId, StringComparison.OrdinalIgnoreCase))?.Name ?? modId;
    }
}
