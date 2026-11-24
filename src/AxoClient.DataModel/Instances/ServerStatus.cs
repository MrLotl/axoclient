namespace AxoClient.DataModel.Instances
{
    public sealed record ServerStatus(bool Online, int Players, int MaxPlayers, string Motd, long? LatencyMs, string? Favicon, string? Version);
}
