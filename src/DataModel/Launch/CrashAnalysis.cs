using AxoClient.Common;

namespace AxoClient.DataModel.Launch
{
    public sealed record CrashAnalysis(
        bool KnownCause,
        TextRef Cause,
        string? RuleId,
        IReadOnlyList<string> Actions,
        IReadOnlyList<CrashSuspect> Suspects,
        IReadOnlyList<CrashSuspect> RecentlyAdded,
        string? ConfigFile,
        string? CrashReportFile,
        int ExitCode);
}
