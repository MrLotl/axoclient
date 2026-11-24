namespace AxoClient.DataModel.Content
{
    public sealed record ContentUpdate(ContentType Type, string File, string Name, string? CurrentVersion, string NewVersion,
        string NewVersionId, string Source, string ProjectId, string? IconUrl);
}
