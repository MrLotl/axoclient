using System.Collections.Concurrent;
using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.Launch.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Zeigt Warnungen und Blocker der Start-Pipeline als Dialog in der UI und wartet auf die Antwort.</summary>
    public sealed class LaunchPromptBridge : ILaunchPrompt
    {
        private readonly IEventHub _events;
        private readonly IAppLog _appLog;
        /// <summary>So lange darf es dauern, bis die Oberfläche meldet, dass der Dialog wirklich zu sehen ist.</summary>
        private readonly TimeSpan _showTimeout = TimeSpan.FromSeconds(10);

        private readonly ConcurrentDictionary<string, TaskCompletionSource<PromptDecision>> _pending = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _shown = new();

        public LaunchPromptBridge(IEventHub events, IAppLog appLog)
        {
            _events = events;
            _appLog = appLog;
        }

        public async Task<PromptDecision> Ask(string launchId, string instanceId, LaunchPhase phase, IReadOnlyList<IssueView> issues,
            CancellationToken cancellationToken)
        {
            var promptId = $"{launchId}:{phase}:{Guid.NewGuid():N}"[..40];
            var completionSource = new TaskCompletionSource<PromptDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[promptId] = completionSource;
            _shown[promptId] = shown;
            await using var _ = cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));
            var summary = string.Join("; ", issues.Select(issue => $"{issue.Severity} {issue.Message}"));
            _appLog.Info($"Start {launchId}: Rückfrage ({phase}) mit {issues.Count} Punkt(en): {summary}");
            try
            {
                // Kommt die Rückfrage nicht bei der Oberfläche an oder wird dort nicht angezeigt, würde der Start sonst ewig
                // auf eine Antwort warten (Fortschritt bleibt einfach stehen). Dann lieber mit Meldung abbrechen.
                if (!_events.TryPublish("launch.prompt", new { promptId, instanceId, phase, issues }))
                {
                    throw new AxoException("pipeline.promptFailed");
                }

                var first = await Task.WhenAny(shown.Task, completionSource.Task, Task.Delay(_showTimeout, cancellationToken));
                if (first != shown.Task && first != completionSource.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _appLog.Error($"Start {launchId}: Rückfrage ({phase}) wurde von der Oberfläche nicht bestätigt");
                    throw new AxoException("pipeline.promptFailed");
                }
                return await completionSource.Task;
            }
            finally
            {
                _pending.TryRemove(promptId, out var _);
                _shown.TryRemove(promptId, out var _);
                _events.Publish("launch.promptClosed", new { promptId });
            }
        }

        /// <summary>Die Oberfläche zeigt den Dialog an.</summary>
        public bool Shown(string promptId) => _shown.TryGetValue(promptId, out var completionSource) && completionSource.TrySetResult();

        public bool Resolve(string promptId, PromptDecision decision) =>
            _pending.TryGetValue(promptId, out var completionSource) && completionSource.TrySetResult(decision);
    }
}
