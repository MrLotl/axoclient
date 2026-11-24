namespace AxoClient.DataModel.Updates
{
    public sealed record UpdateView(string Version, long SizeBytes, string? Notes, bool Downloaded);
}
