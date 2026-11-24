using AxoClient.DataModel.Content;
using AxoClient.DataModel.Launch;

namespace AxoClient.Launch.Contract
{
    public interface ICrashAnalyzer
    {
        CrashRuleSet LoadEmbeddedRules();

        IReadOnlyList<CrashRule> Rules { get; }

        CrashAnalysis Analyze(CrashInput input, ContentDocument? content, bool server = false);

        /// <summary>Liest neue Crash-Reports und hs_err-Dateien seit dem Start.</summary>
        (string? CrashText, string? CrashFile, string? HsErr) CollectFiles(string gameDirectory, DateTimeOffset since);
    }
}
