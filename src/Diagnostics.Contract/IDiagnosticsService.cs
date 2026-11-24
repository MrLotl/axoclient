using AxoClient.Common;
using AxoClient.DataModel.Diagnostics;

namespace AxoClient.Diagnostics.Contract
{
    public interface IDiagnosticsService
    {
        Task<IReadOnlyList<DiagnosticItem>> Run(JobContext job);

        /// <summary>"Bericht kopieren": lesbar und geschwärzt (Tokens, Spielername, UUID, IP-Adressen).</summary>
        string Report(IReadOnlyList<DiagnosticItem> items, Func<TextRef, string> translate);
    }
}
