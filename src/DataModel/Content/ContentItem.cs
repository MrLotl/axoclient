namespace AxoClient.DataModel.Content
{
    /// <summary>Ein Inhalt, wie ihn die UI-Liste anzeigt.</summary>
    public sealed record ContentItem(
        string File,
        string Name,
        string? Version,
        bool Enabled,
        ContentSource Source,
        string? ProjectId,
        string? VersionId,
        string? IconUrl,
        string? Author,
        long Size,
        DateTimeOffset AddedAt,
        bool Locked,
        bool ModLocked,
        string? PreviousVersion = null);
}
