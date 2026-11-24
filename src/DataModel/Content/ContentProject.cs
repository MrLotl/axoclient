namespace AxoClient.DataModel.Content
{
    public sealed record ContentProject(
        string Source,
        string Id,
        string Slug,
        string Title,
        string Author,
        string Description,
        string? IconUrl,
        string? ImageUrl,
        long Downloads,
        IReadOnlyList<string> Categories,
        IReadOnlyList<string> Loaders,
        IReadOnlyList<string> GameVersions,
        DateTimeOffset? Updated,
        string WebUrl);
}
