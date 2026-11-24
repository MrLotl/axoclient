using AxoClient.DataModel.Launch;

namespace AxoClient.Launch.Contract
{
    /// <summary>Brücke zur UI: zeigt Warnungen und Blocker einer Phase gesammelt in einem Dialog.</summary>
    public interface ILaunchPrompt
    {
        Task<PromptDecision> Ask(string launchId, string instanceId, LaunchPhase phase, IReadOnlyList<IssueView> issues,
            CancellationToken ct);
    }
}
