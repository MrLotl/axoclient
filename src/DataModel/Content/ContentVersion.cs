namespace AxoClient.DataModel.Content
{
    public sealed record ContentVersion(
        string Source,
        string Id,
        string ProjectId,
        string Name,
        string VersionNumber,
        string Channel,
        DateTimeOffset Published,
        IReadOnlyList<string> GameVersions,
        IReadOnlyList<string> Loaders,
        IReadOnlyList<ContentFile> Files,
        IReadOnlyList<ContentDependency> Dependencies,
        string? Changelog = null)
    {
        public ContentFile? PrimaryFile => Files.FirstOrDefault(file => file.Primary) ?? Files.FirstOrDefault();
    }
}
