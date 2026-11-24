using AxoClient.DataModel.Launch;

namespace AxoClient.Launch.Contract
{
    /// <summary>Eine Prüfung vor dem Start. Testbar: liefert nur Ergebnisse, Reparaturen sind Maßnahmen.</summary>
    public interface ILaunchCheck
    {
        string Id { get; }
        LaunchPhase Phase { get; }

        Task<IReadOnlyList<CheckResult>> Run(LaunchContext context, CancellationToken cancellationToken);
    }
}
