namespace AxoClient.DataModel.Launch
{
    /// <summary>Material für die Analyse: Exit-Code, Crash-Report bzw. hs_err und die letzten Log-Zeilen.</summary>
    public sealed record CrashInput(int ExitCode, string? CrashReport, string? HsErr, string LogTail, string? CrashReportFile);
}
