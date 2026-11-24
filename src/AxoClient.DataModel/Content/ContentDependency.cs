namespace AxoClient.DataModel.Content
{
    public sealed record ContentDependency(string? ProjectId, string? VersionId, DependencyKind Kind);
}
