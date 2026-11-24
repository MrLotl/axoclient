using AxoClient.Common;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Launch;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Settings.Contract;

namespace AxoClient.Launch
{
    /// <summary>
    /// Läuft Phase für Phase durch alle <see cref="ILaunchCheck"/>s: Auto-Fixes still, Warnungen und Blocker einer Phase
    /// gesammelt in einem Dialog. Ein fehlgeschlagener Auto-Fix wird zum Blocker mit "Erneut versuchen".
    /// </summary>
    public sealed class LaunchPipeline : ILaunchPipeline
    {
        private readonly ILaunchPrompt _prompt;
        private readonly IInstanceManager _instances;
        private readonly IMinecraftInstaller _installer;
        private readonly ISettingsService _settings;
        private readonly IAppLog _appLog;
        private readonly IExceptionHelper _exceptionHelper;
        private const int MaxRoundsPerPhase = 4;
        private readonly IReadOnlyList<ILaunchCheck> _checks;

        /// <summary>Führt alle Prüfungen aus. Gibt den vorbereiteten Kontext zurück oder wirft bei Abbruch.</summary>
        public async Task<LaunchContext> Prepare(string instanceId, QuickPlay? quickPlay, JobContext job)
        {
            var instance = await _instances.Get(instanceId, job.Token);
            var gameDirectory = _instances.GameDirectory(instanceId);
            var context = new LaunchContext
            {
                LaunchId = Guid.NewGuid().ToString("N")[..12],
                Instance = instance,
                Content = await _instances.ContentOf(instanceId).Load(job.Token),
                Settings = _settings.Current,
                GamePath = _installer.PathFor(gameDirectory),
                GameDir = gameDirectory,
                QuickPlay = quickPlay
            };
            await runChecks(context, job, startPhase: LaunchPhase.Environment, endPhase: LaunchPhase.Resources, interactive: true);

            // Eine Maßnahme kann Loader oder Version geändert haben: dann jetzt nachinstallieren
            if (context.VersionName == null || context.Instance.Loader.Version == null && context.Instance.Loader.Type != LoaderType.Vanilla)
            {
                await runChecks(context, job, LaunchPhase.GameFiles, LaunchPhase.GameFiles, interactive: true);
                if (context.VersionName == null)
                {
                    throw new AxoException("pipeline.gameFiles.failed");
                }
            }

            // Geprüft: bis zur nächsten Änderung müssen Kompatibilität und Abhängigkeiten nicht erneut laufen
            context.Content = await _instances.ContentOf(instanceId).Update(document => document.CheckedAt = DateTimeOffset.UtcNow, job.Token);
            return context;
        }

        /// <summary>Prüfungen, die nur beim Start Sinn ergeben (Konto, laufendes Spiel, Internet) oder still mitlaufen.</summary>
        private readonly HashSet<string> _notARepair = ["account", "running", "internet", "disk", "gameFiles", "axomod", "joinRelay",
            "unknownFiles"];

        /// <summary>
        /// Ob ein Ergebnis im Mods-Tab als "muss repariert werden" zählt. Einrichtungsschritte, die beim Start ohnehin still
        /// laufen (AxoClient-Mod, e4mc, neue Dateien eintragen), schwankende Werte (gerade freier Arbeitsspeicher) und
        /// stummgeschaltete Warnungen gehören nicht dazu – sonst meldet auch ein einwandfreies Modpack ständig ein Problem.
        /// </summary>
        public bool IsRepairIssue(CheckResult result, ContentDocument content)
        {
            if (result.Severity == Severity.Ok || _notARepair.Contains(result.CheckId))
            {
                return false;
            }
            // Auto-Fixes laufen beim Start still; nur fehlende oder beschädigte Mod-Dateien sind ein echtes Problem
            if (result.Severity == Severity.AutoFix)
            {
                return result.CheckId == "contentFiles";
            }

            if (result.Key == "ram-not-free")
            {
                return false;
            }

            return !(result.Severity == Severity.Warning && result.CanSilence && result.Key != null && content.IgnoredWarnings.Contains(result.Key));
        }

        /// <summary>Für die Diagnose: alle Prüfungen einer Instanz ohne Dialog und ohne Reparatur.</summary>
        public async Task<IReadOnlyList<CheckResult>> Diagnose(string instanceId, CancellationToken cancellationToken) =>
            (await collectIssues(instanceId, "diagnose", cancellationToken)).Issues;

        /// <summary>
        /// Ein einzelnes Problem aus der Diagnose beheben. <paramref name="index"/> ist die Stelle in der Liste von
        /// <see cref="Diagnose"/>; die Prüfungen laufen dafür neu, damit die Maßnahme zum aktuellen Stand passt.
        /// </summary>
        public async Task RepairOne(string instanceId, string checkId, int index, string? actionId, JobContext job)
        {
            var (context, issues) = await collectIssues(instanceId, "repair", job.Token);
            if (index < 0 || index >= issues.Count || issues[index].CheckId != checkId)
            {
                throw new AxoException("content.issueGone");
            }

            var issue = issues[index];
            var action = issue.Actions.FirstOrDefault(candidateAction => candidateAction.Id == actionId)
                         ?? issue.Actions.FirstOrDefault(candidateAction => candidateAction.Primary) ?? issue.Actions.FirstOrDefault()
                         ?? throw new AxoException("content.issueGone");
            _appLog.Info($"Reparatur einzeln: {issue.CheckId}/{action.Id} ({issue.Message.Code})");
            await action.Run(context, job.Sub(0, 1));
        }

        private async Task<(LaunchContext Ctx, List<CheckResult> Issues)> collectIssues(string instanceId, string launchId,
            CancellationToken cancellationToken)
        {
            var instance = await _instances.Get(instanceId, cancellationToken);
            var gameDirectory = _instances.GameDirectory(instanceId);
            var context = new LaunchContext
            {
                LaunchId = launchId,
                Instance = instance,
                Content = await _instances.ContentOf(instanceId).Load(cancellationToken),
                Settings = _settings.Current,
                GamePath = _installer.PathFor(gameDirectory),
                GameDir = gameDirectory
            };
            var results = new List<CheckResult>();
            foreach (var check in _checks.Where(check => !_notARepair.Contains(check.Id)))
            {
                try
                {
                    results.AddRange(await check.Run(context, cancellationToken));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Eine Prüfung, die selbst scheitert (z. B. Modrinth nicht erreichbar), ist kein Problem der Instanz
                    _appLog.Warn($"Prüfung {check.Id} für die Diagnose fehlgeschlagen", exception);
                }
            }
            return (context, results.Where(result => IsRepairIssue(result, context.Content)).ToList());
        }

        /// <summary>"Alles reparieren" im Mods-Tab: für jedes gefundene Problem die erste (empfohlene) Maßnahme ausführen.</summary>
        public async Task<int> RepairAll(string instanceId, JobContext job)
        {
            var instance = await _instances.Get(instanceId, job.Token);
            var gameDirectory = _instances.GameDirectory(instanceId);
            var context = new LaunchContext
            {
                LaunchId = "repair",
                Instance = instance,
                Content = await _instances.ContentOf(instanceId).Load(job.Token),
                Settings = _settings.Current,
                GamePath = _installer.PathFor(gameDirectory),
                GameDir = gameDirectory
            };
            var fixedCount = 0;
            foreach (var phase in new[] { LaunchPhase.Content, LaunchPhase.Compatibility, LaunchPhase.Resources })
            {
                var results = await runPhase(context, phase, job.Token);
                // Genau das reparieren, was die Diagnose als Problem zeigt – nicht still nebenbei RAM senken o. Ä.
                foreach (var result in results.Where(result => IsRepairIssue(result, context.Content) && result.Actions.Count > 0))
                {
                    var action = result.Actions.FirstOrDefault(candidateAction => candidateAction.Primary) ?? result.Actions[0];
                    if (action.Id is "switch" or "retry" or "login")
                    {
                        continue;
                    }

                    try
                    {
                        await action.Run(context, job.Sub(0, 1));
                        fixedCount++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // Eine Maßnahme, die nicht klappt (z. B. keine passende Version), hält die übrigen nicht auf
                        _appLog.Warn($"Reparatur {result.CheckId}/{action.Id} fehlgeschlagen", exception);
                    }
                }
                context.Content = await _instances.ContentOf(instanceId).Load(job.Token);
                context.Instance = await _instances.Get(instanceId, job.Token);
            }
            return fixedCount;
        }

        private async Task runChecks(LaunchContext context, JobContext job, LaunchPhase startPhase, LaunchPhase endPhase, bool interactive)
        {
            var phases = Enum.GetValues<LaunchPhase>().Where(value => value >= startPhase && value <= endPhase).ToList();
            // Warnungen, auf die der Nutzer bei diesem Start schon reagiert hat, nicht erneut fragen. Sonst kommt z. B.
            // "nur X MB frei" nach "RAM senken" sofort wieder, weil der freie Arbeitsspeicher ständig schwankt.
            var answered = new HashSet<string>();
            for (var phaseIndex = 0; phaseIndex < phases.Count; phaseIndex++)
            {
                var phase = phases[phaseIndex];
                var progressFrom = (double)phaseIndex / phases.Count;
                var progressTo = (double)(phaseIndex + 1) / phases.Count;
                job.Report(progressFrom, new TextRef($"pipeline.phase.{char.ToLowerInvariant(phase.ToString()[0])}{phase.ToString()[1..]}"));

                for (var round = 0; ; round++)
                {
                    var results = await runPhase(context, phase, job.Token);

                    // Auto-Fixes in Prüfungsreihenfolge ausführen
                    var failedFixes = new List<CheckResult>();
                    foreach (var fix in results.Where(result => result.Severity == Severity.AutoFix))
                    {
                        try
                        {
                            job.Report(progressFrom, fix.Message);
                            _appLog.Info($"Start {context.LaunchId}: Auto-Fix {fix.CheckId} ({fix.Message.Code})");
                            var sub = new JobContextScope(job, progressFrom, progressTo);
                            await fix.AutoFix!.Run(context, sub.Context);
                        }
                        catch (AxoException exception) when (exception.Code.StartsWith("content.modLock.", StringComparison.Ordinal))
                        {
                            // Mod-Schutz: nicht automatisch an gesperrten Mods drehen, Spiel trotzdem starten
                            context.Hints.Add(new TextRef("pipeline.modLockSkipped", ("what", fix.Message.Code)));
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException and not LaunchAbortedException)
                        {
                            _appLog.Warn($"Auto-Fix {fix.CheckId} fehlgeschlagen", exception);
                            // Bei Mods (z. B. eine Abhängigkeit gibt es nicht für diese Version) darf man trotzdem starten;
                            // fehlen Spieldateien oder der Loader, geht es nicht.
                            var severity = phase is LaunchPhase.Content or LaunchPhase.Compatibility ? Severity.Warning : Severity.Blocker;
                            failedFixes.Add(new CheckResult(fix.CheckId, severity,
                                new TextRef("pipeline.autoFixFailed", ("what", fix.Message.Code), ("reason", _exceptionHelper.Describe(exception).Code)),
                                [new CheckAction("retry", new TextRef("pipeline.action.retry"), (_, _) => Task.CompletedTask, true)],
                                CanSilence: false, Detail: _exceptionHelper.Describe(exception)));
                        }
                    }
                    if (results.Any(result => result.Severity == Severity.AutoFix))
                    {
                        context.Content = await _instances.ContentOf(context.Instance.Id).Load(job.Token);
                    }

                    var ignored = context.Content.IgnoredWarnings.ToHashSet();
                    var issues = results
                        .Where(result => result.Severity == Severity.Blocker
                                    || result.Severity == Severity.Warning && !(result.CanSilence && result.Key != null && ignored.Contains(result.Key))
                                                                     && !answered.Contains(warningKey(result)))
                        .Concat(failedFixes)
                        .ToList();
                    if (issues.Count == 0)
                    {
                        break;
                    }

                    if (!interactive || round >= MaxRoundsPerPhase)
                    {
                        throw new AxoException("pipeline.blocked");
                    }

                    var views = issues.Select((issue, index) => toView(issue, index)).ToList();
                    var decision = await _prompt.Ask(context.LaunchId, context.Instance.Id, phase, views, job.Token);
                    if (decision.Choice == PromptChoice.Cancel)
                    {
                        throw new OperationCanceledException();
                    }

                    foreach (var warning in issues.Where(issue => issue.Severity == Severity.Warning))
                    {
                        answered.Add(warningKey(warning));
                    }

                    var hasBlocker = issues.Any(issue => issue.Severity == Severity.Blocker);
                    if (decision.Choice == PromptChoice.StartAnyway && !hasBlocker)
                    {
                        await silence(context, issues, views, decision.Silence);
                        break;
                    }

                    // Gewählte Maßnahmen ausführen; ohne Auswahl die jeweils erste
                    for (var index = 0; index < issues.Count; index++)
                    {
                        var issue = issues[index];
                        var actionId = decision.Actions?.GetValueOrDefault(views[index].Id);
                        if (decision.Choice == PromptChoice.StartAnyway && issue.Severity == Severity.Warning)
                        {
                            continue;
                        }

                        var action = issue.Actions.FirstOrDefault(candidateAction => candidateAction.Id == actionId) ?? issue.Actions.FirstOrDefault();
                        if (action == null)
                        {
                            continue;
                        }

                        job.Report(progressFrom, action.Label);
                        await action.Run(context, new JobContextScope(job, progressFrom, progressTo).Context);
                    }
                    await silence(context, issues, views, decision.Silence);
                    context.Content = await _instances.ContentOf(context.Instance.Id).Load(job.Token);
                    context.Instance = await _instances.Get(context.Instance.Id, job.Token);
                }
            }
            job.Report(1, new TextRef("pipeline.phase.ready"));
        }

        private string warningKey(CheckResult result) => result.Key ?? $"{result.CheckId}:{result.Message.Code}";

        /// <summary>Höchstdauer einer einzelnen Prüfung; Spieldateien (Download, Loader-Installation) laufen als Auto-Fix und zählen nicht.</summary>
        private readonly TimeSpan _checkTimeout = TimeSpan.FromSeconds(45);

        public LaunchPipeline(
            IEnumerable<ILaunchCheck> checks,
            ILaunchPrompt prompt,
            IInstanceManager instances,
            IMinecraftInstaller installer,
            ISettingsService settings,
            IAppLog appLog,
            IExceptionHelper exceptionHelper)
        {
            _prompt = prompt;
            _instances = instances;
            _installer = installer;
            _settings = settings;
            _appLog = appLog;
            _exceptionHelper = exceptionHelper;
            _checks = checks.ToList();
        }

        private async Task<List<CheckResult>> runPhase(LaunchContext context, LaunchPhase phase, CancellationToken cancellationToken)
        {
            var results = new List<CheckResult>();
            foreach (var check in _checks.Where(check => check.Phase == phase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var started = System.Diagnostics.Stopwatch.StartNew();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_checkTimeout);
                try
                {
                    // WaitAsync: auch eine Prüfung, die das Token nicht beachtet, hält den Start nicht auf
                    // Task.Run: das Zeitlimit greift auch, wenn eine Prüfung vor ihrem ersten await lange rechnet oder liest
                    results.AddRange(await Task.Run(() => check.Run(context, timeout.Token), cancellationToken).WaitAsync(_checkTimeout, cancellationToken));
                }
                catch (Exception exception) when (exception is TimeoutException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    // Eine Prüfung, die hängt (z. B. Modrinth antwortet nicht), darf den Start nicht blockieren
                    _appLog.Warn($"Start {context.LaunchId}: Prüfung {check.Id} nach {started.Elapsed.TotalSeconds:0} s abgebrochen");
                    context.Hints.Add(new TextRef("pipeline.checkSkipped", ("check", check.Id)));
                    continue;
                }
                _appLog.Info($"Start {context.LaunchId}: Prüfung {check.Id} fertig ({started.ElapsedMilliseconds} ms)");
            }
            return results;
        }

        private async Task silence(LaunchContext context, List<CheckResult> issues, List<IssueView> views, IReadOnlyCollection<string>? silence)
        {
            if (silence is not { Count: > 0 })
            {
                return;
            }

            var keys = issues.Where((issue, index) => issue.Severity == Severity.Warning && issue.CanSilence && issue.Key != null && silence.Contains(views[index].Id))
                .Select(issue => issue.Key!).ToList();
            if (keys.Count == 0)
            {
                return;
            }

            await _instances.ContentOf(context.Instance.Id).Update(document =>
            {
                foreach (var key in keys.Where(key => !document.IgnoredWarnings.Contains(key)))
                {
                    document.IgnoredWarnings.Add(key);
                }
            });
        }

        private IssueView toView(CheckResult result, int index) => new(
            $"{result.CheckId}-{index}", result.Severity, result.Message, result.Detail,
            result.Actions.Select(action => new ActionView(action.Id, action.Label, action.Primary)).ToList(),
            result.Severity == Severity.Warning && result.CanSilence && result.Key != null);

        /// <summary>Gibt einem Auto-Fix einen Teil des Fortschrittsbalkens.</summary>
        private sealed class JobContextScope
        {
            public JobContextScope(JobContext parent, double from, double to)
            {
                Context = parent.Sub(from, to);
            }

            public JobContext Context { get; }
        }
    }
}
