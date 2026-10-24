using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;
using AxoClient.Core.Launch.Checks;

namespace AxoClient.Core.Launch;

/// <summary>
/// Läuft Phase für Phase durch alle <see cref="ILaunchCheck"/>s: Auto-Fixes still, Warnungen und Blocker einer Phase
/// gesammelt in einem Dialog. Ein fehlgeschlagener Auto-Fix wird zum Blocker mit "Erneut versuchen".
/// </summary>
public sealed class LaunchPipeline(
    IEnumerable<ILaunchCheck> checks,
    ILaunchPrompt prompt,
    InstanceManager instances,
    GameInstaller installer,
    SettingsService settings)
{
    private const int MaxRoundsPerPhase = 4;
    private readonly IReadOnlyList<ILaunchCheck> _checks = checks.ToList();

    /// <summary>Führt alle Prüfungen aus. Gibt den vorbereiteten Kontext zurück oder wirft bei Abbruch.</summary>
    public async Task<LaunchContext> PrepareAsync(string instanceId, QuickPlay? quickPlay, JobContext job)
    {
        var instance = await instances.GetAsync(instanceId, job.Token);
        var gameDir = instances.GameDir(instanceId);
        var ctx = new LaunchContext
        {
            LaunchId = Guid.NewGuid().ToString("N")[..12],
            Instance = instance,
            Content = await instances.ContentOf(instanceId).LoadAsync(job.Token),
            Settings = settings.Current,
            GamePath = installer.PathFor(gameDir),
            GameDir = gameDir,
            QuickPlay = quickPlay
        };
        await RunChecksAsync(ctx, job, startPhase: LaunchPhase.Environment, endPhase: LaunchPhase.Resources, interactive: true);

        // Eine Maßnahme kann Loader oder Version geändert haben: dann jetzt nachinstallieren
        if (ctx.VersionName == null || ctx.Instance.Loader.Version == null && ctx.Instance.Loader.Type != LoaderType.Vanilla)
        {
            await RunChecksAsync(ctx, job, LaunchPhase.GameFiles, LaunchPhase.GameFiles, interactive: true);
            if (ctx.VersionName == null)
                throw new AxoException("pipeline.gameFiles.failed");
        }

        // Geprüft: bis zur nächsten Änderung müssen Kompatibilität und Abhängigkeiten nicht erneut laufen
        ctx.Content = await instances.ContentOf(instanceId).UpdateAsync(doc => doc.CheckedAt = DateTimeOffset.UtcNow, job.Token);
        return ctx;
    }

    /// <summary>Für die Diagnose: alle Prüfungen einer Instanz ohne Dialog und ohne Reparatur.</summary>
    public async Task<IReadOnlyList<CheckResult>> DiagnoseAsync(string instanceId, CancellationToken ct)
    {
        var instance = await instances.GetAsync(instanceId, ct);
        var gameDir = instances.GameDir(instanceId);
        var ctx = new LaunchContext
        {
            LaunchId = "diagnose",
            Instance = instance,
            Content = await instances.ContentOf(instanceId).LoadAsync(ct),
            Settings = settings.Current,
            GamePath = installer.PathFor(gameDir),
            GameDir = gameDir
        };
        var results = new List<CheckResult>();
        foreach (var check in _checks.Where(c => c is not AccountCheck and not AlreadyRunningCheck and not InternetCheck))
        {
            try
            {
                results.AddRange(await check.RunAsync(ctx, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new CheckResult(check.Id, Severity.Warning, AxoException.Describe(ex), []));
            }
        }
        return results;
    }

    /// <summary>"Alles reparieren" im Mods-Tab: für jedes gefundene Problem die erste (empfohlene) Maßnahme ausführen.</summary>
    public async Task<int> RepairAllAsync(string instanceId, JobContext job)
    {
        var instance = await instances.GetAsync(instanceId, job.Token);
        var gameDir = instances.GameDir(instanceId);
        var ctx = new LaunchContext
        {
            LaunchId = "repair",
            Instance = instance,
            Content = await instances.ContentOf(instanceId).LoadAsync(job.Token),
            Settings = settings.Current,
            GamePath = installer.PathFor(gameDir),
            GameDir = gameDir
        };
        var fixedCount = 0;
        foreach (var phase in new[] { LaunchPhase.Content, LaunchPhase.Compatibility, LaunchPhase.Resources })
        {
            var results = await RunPhaseAsync(ctx, phase, job.Token);
            foreach (var result in results.Where(r => r.Severity != Severity.Ok && r.Actions.Count > 0))
            {
                var action = result.Actions.FirstOrDefault(a => a.Primary) ?? result.Actions[0];
                if (action.Id is "switch" or "retry" or "login")
                    continue;
                await action.Run(ctx, job.Sub(0, 1));
                fixedCount++;
            }
            ctx.Content = await instances.ContentOf(instanceId).LoadAsync(job.Token);
            ctx.Instance = await instances.GetAsync(instanceId, job.Token);
        }
        return fixedCount;
    }

    private async Task RunChecksAsync(LaunchContext ctx, JobContext job, LaunchPhase startPhase, LaunchPhase endPhase, bool interactive)
    {
        var phases = Enum.GetValues<LaunchPhase>().Where(p => p >= startPhase && p <= endPhase).ToList();
        // Warnungen, auf die der Nutzer bei diesem Start schon reagiert hat, nicht erneut fragen. Sonst kommt z. B.
        // "nur X MB frei" nach "RAM senken" sofort wieder, weil der freie Arbeitsspeicher ständig schwankt.
        var answered = new HashSet<string>();
        for (var phaseIndex = 0; phaseIndex < phases.Count; phaseIndex++)
        {
            var phase = phases[phaseIndex];
            var progressFrom = (double)phaseIndex / phases.Count;
            var progressTo = (double)(phaseIndex + 1) / phases.Count;
            job.Report(progressFrom, new TextRef($"pipeline.phase.{char.ToLowerInvariant(phase.ToString()[0])}{phase.ToString()[1..]}"));

            for (var round = 0;; round++)
            {
                var results = await RunPhaseAsync(ctx, phase, job.Token);

                // Auto-Fixes in Prüfungsreihenfolge ausführen
                var failedFixes = new List<CheckResult>();
                foreach (var fix in results.Where(r => r.Severity == Severity.AutoFix))
                {
                    try
                    {
                        job.Report(progressFrom, fix.Message);
                        var sub = new JobContextScope(job, progressFrom, progressTo);
                        await fix.AutoFix!.Run(ctx, sub.Context);
                    }
                    catch (AxoException ex) when (ex.Code.StartsWith("content.modLock.", StringComparison.Ordinal))
                    {
                        // Mod-Schutz: nicht automatisch an gesperrten Mods drehen, Spiel trotzdem starten
                        ctx.Hints.Add(TextRef.Of("pipeline.modLockSkipped", ("what", fix.Message.Code)));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not LaunchAbortedException)
                    {
                        AppLog.Warn($"Auto-Fix {fix.CheckId} fehlgeschlagen", ex);
                        failedFixes.Add(new CheckResult(fix.CheckId, Severity.Blocker,
                            TextRef.Of("pipeline.autoFixFailed", ("what", fix.Message.Code), ("reason", AxoException.Describe(ex).Code)),
                            [new CheckAction("retry", new TextRef("pipeline.action.retry"), (_, _) => Task.CompletedTask, true)],
                            CanSilence: false, Detail: AxoException.Describe(ex)));
                    }
                }
                if (results.Any(r => r.Severity == Severity.AutoFix))
                    ctx.Content = await instances.ContentOf(ctx.Instance.Id).LoadAsync(job.Token);

                var ignored = ctx.Content.IgnoredWarnings.ToHashSet();
                var issues = results
                    .Where(r => r.Severity == Severity.Blocker
                                || r.Severity == Severity.Warning && !(r.CanSilence && r.Key != null && ignored.Contains(r.Key))
                                                                 && !answered.Contains(WarningKey(r)))
                    .Concat(failedFixes)
                    .ToList();
                if (issues.Count == 0)
                    break;
                if (!interactive || round >= MaxRoundsPerPhase)
                    throw new AxoException("pipeline.blocked");

                var views = issues.Select((r, i) => ToView(r, i)).ToList();
                var decision = await prompt.AskAsync(ctx.LaunchId, ctx.Instance.Id, phase, views, job.Token);
                if (decision.Choice == PromptChoice.Cancel)
                    throw new OperationCanceledException();

                foreach (var warning in issues.Where(r => r.Severity == Severity.Warning))
                    answered.Add(WarningKey(warning));

                var hasBlocker = issues.Any(r => r.Severity == Severity.Blocker);
                if (decision.Choice == PromptChoice.StartAnyway && !hasBlocker)
                {
                    await SilenceAsync(ctx, issues, views, decision.Silence);
                    break;
                }

                // Gewählte Maßnahmen ausführen; ohne Auswahl die jeweils erste
                for (var i = 0; i < issues.Count; i++)
                {
                    var issue = issues[i];
                    var actionId = decision.Actions?.GetValueOrDefault(views[i].Id);
                    if (decision.Choice == PromptChoice.StartAnyway && issue.Severity == Severity.Warning)
                        continue;
                    var action = issue.Actions.FirstOrDefault(a => a.Id == actionId) ?? issue.Actions.FirstOrDefault();
                    if (action == null)
                        continue;
                    job.Report(progressFrom, action.Label);
                    await action.Run(ctx, new JobContextScope(job, progressFrom, progressTo).Context);
                }
                await SilenceAsync(ctx, issues, views, decision.Silence);
                ctx.Content = await instances.ContentOf(ctx.Instance.Id).LoadAsync(job.Token);
                ctx.Instance = await instances.GetAsync(ctx.Instance.Id, job.Token);
            }
        }
        job.Report(1, new TextRef("pipeline.phase.ready"));
    }

    private static string WarningKey(CheckResult r) => r.Key ?? $"{r.CheckId}:{r.Message.Code}";

    private async Task<List<CheckResult>> RunPhaseAsync(LaunchContext ctx, LaunchPhase phase, CancellationToken ct)
    {
        var results = new List<CheckResult>();
        foreach (var check in _checks.Where(c => c.Phase == phase))
        {
            ct.ThrowIfCancellationRequested();
            results.AddRange(await check.RunAsync(ctx, ct));
        }
        return results;
    }

    private async Task SilenceAsync(LaunchContext ctx, List<CheckResult> issues, List<IssueView> views, IReadOnlyCollection<string>? silence)
    {
        if (silence is not { Count: > 0 })
            return;
        var keys = issues.Where((r, i) => r.Severity == Severity.Warning && r.CanSilence && r.Key != null && silence.Contains(views[i].Id))
            .Select(r => r.Key!).ToList();
        if (keys.Count == 0)
            return;
        await instances.ContentOf(ctx.Instance.Id).UpdateAsync(doc =>
        {
            foreach (var key in keys.Where(k => !doc.IgnoredWarnings.Contains(k)))
                doc.IgnoredWarnings.Add(key);
        });
    }

    private static IssueView ToView(CheckResult r, int index) => new(
        $"{r.CheckId}-{index}", r.Severity, r.Message, r.Detail,
        r.Actions.Select(a => new ActionView(a.Id, a.Label, a.Primary)).ToList(),
        r.Severity == Severity.Warning && r.CanSilence && r.Key != null);

    /// <summary>Gibt einem Auto-Fix einen Teil des Fortschrittsbalkens.</summary>
    private sealed class JobContextScope(JobContext parent, double from, double to)
    {
        public JobContext Context { get; } = parent.Sub(from, to);
    }
}
