namespace AxoClient.DataModel.Content
{
    /// <summary>Ein Datapack einer Welt, wie ihn die UI-Liste anzeigt.</summary>
    public sealed record DatapackItem(
        string File,
        string Name,
        string? Version,
        bool Enabled,
        bool IsFolder,
        ContentSource Source,
        string? ProjectId,
        string? IconUrl,
        string? Author,
        long Size);
}
