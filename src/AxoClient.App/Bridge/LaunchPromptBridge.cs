using System.Collections.Concurrent;
using AxoClient.Core.Common;
using AxoClient.Core.Launch;

namespace AxoClient.App.Bridge;

/// <summary>Zeigt Warnungen und Blocker der Start-Pipeline als Dialog in der UI und wartet auf die Antwort.</summary>
public sealed class LaunchPromptBridge(EventHub events) : ILaunchPrompt
{
    /// <summary>So lange darf es dauern, bis die Oberfläche meldet, dass der Dialog wirklich zu sehen ist.</summary>
    private static readonly TimeSpan ShowTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<PromptDecision>> _pending = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _shown = new();

    public async Task<PromptDecision> AskAsync(string launchId, string instanceId, LaunchPhase phase, IReadOnlyList<IssueView> issues,
        CancellationToken ct)
    {
        var promptId = $"{launchId}:{phase}:{Guid.NewGuid():N}"[..40];
        var tcs = new TaskCompletionSource<PromptDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[promptId] = tcs;
        _shown[promptId] = shown;
        await using var _ = ct.Register(() => tcs.TrySetCanceled(ct));
        var summary = string.Join("; ", issues.Select(i => $"{i.Severity} {i.Message}"));
        AppLog.Info($"Start {launchId}: Rückfrage ({phase}) mit {issues.Count} Punkt(en): {summary}");
        try
        {
            // Kommt die Rückfrage nicht bei der Oberfläche an oder wird dort nicht angezeigt, würde der Start sonst ewig
            // auf eine Antwort warten (Fortschritt bleibt einfach stehen). Dann lieber mit Meldung abbrechen.
            if (!events.TryPublish("launch.prompt", new { promptId, instanceId, phase, issues }))
                throw new AxoException("pipeline.promptFailed");
            var first = await Task.WhenAny(shown.Task, tcs.Task, Task.Delay(ShowTimeout, ct));
            if (first != shown.Task && first != tcs.Task)
            {
                ct.ThrowIfCancellationRequested();
                AppLog.Error($"Start {launchId}: Rückfrage ({phase}) wurde von der Oberfläche nicht bestätigt");
                throw new AxoException("pipeline.promptFailed");
            }
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(promptId, out var _);
            _shown.TryRemove(promptId, out var _);
            events.Publish("launch.promptClosed", new { promptId });
        }
    }

    /// <summary>Die Oberfläche zeigt den Dialog an.</summary>
    public bool Shown(string promptId) => _shown.TryGetValue(promptId, out var tcs) && tcs.TrySetResult();

    public bool Resolve(string promptId, PromptDecision decision) =>
        _pending.TryGetValue(promptId, out var tcs) && tcs.TrySetResult(decision);
}
