using System.Collections.Concurrent;
using AxoClient.Core.Common;
using AxoClient.Core.Launch;

namespace AxoClient.App.Bridge;

/// <summary>Zeigt Warnungen und Blocker der Start-Pipeline als Dialog in der UI und wartet auf die Antwort.</summary>
public sealed class LaunchPromptBridge(EventHub events) : ILaunchPrompt
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PromptDecision>> _pending = new();

    public async Task<PromptDecision> AskAsync(string launchId, string instanceId, LaunchPhase phase, IReadOnlyList<IssueView> issues,
        CancellationToken ct)
    {
        var promptId = $"{launchId}:{phase}:{Guid.NewGuid():N}"[..40];
        var tcs = new TaskCompletionSource<PromptDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[promptId] = tcs;
        await using var _ = ct.Register(() => tcs.TrySetCanceled(ct));
        // Kommt die Rückfrage nicht bei der Oberfläche an, würde der Start sonst ewig auf eine Antwort warten
        if (!events.TryPublish("launch.prompt", new { promptId, instanceId, phase, issues }))
        {
            _pending.TryRemove(promptId, out var _);
            throw new AxoException("pipeline.promptFailed");
        }
        AppLog.Info($"Start {launchId}: Rückfrage ({phase}) mit {issues.Count} Punkt(en) angezeigt");
        try
        {
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(promptId, out var _);
            events.Publish("launch.promptClosed", new { promptId });
        }
    }

    public bool Resolve(string promptId, PromptDecision decision) =>
        _pending.TryGetValue(promptId, out var tcs) && tcs.TrySetResult(decision);
}
