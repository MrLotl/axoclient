using AxoClient.DataModel.Diagnostics;

namespace AxoClient.Diagnostics.Contract
{
    /// <summary>Eine Prüfung der eingebauten Diagnose.</summary>
    public interface IDiagnosticCheck
    {
        string Area { get; }
        Task<IReadOnlyList<DiagnosticItem>> Run(CancellationToken cancellationToken);
    }
}
