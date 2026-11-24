namespace AxoClient.DataModel.Launch
{
    /// <summary>Ein Absturz mit Analyse für den Absturz-Dialog.</summary>
    public sealed record CrashEvent(string InstanceId, string InstanceName, CrashAnalysis Analysis);
}
