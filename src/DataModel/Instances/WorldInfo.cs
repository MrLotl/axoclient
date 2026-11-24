namespace AxoClient.DataModel.Instances
{
    public sealed record WorldInfo(
        string Folder,
        string Name,
        string GameMode,
        bool Hardcore,
        bool Cheats,
        string? Version,
        DateTimeOffset? LastPlayed,
        long SizeBytes,
        string? IconUrl);
}
