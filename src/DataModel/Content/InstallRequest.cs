namespace AxoClient.DataModel.Content
{
    /// <summary>Was installiert werden soll: eine Version aus einer Quelle.</summary>
    public sealed record InstallRequest(string Source, string ProjectId, string? VersionId = null);
}
