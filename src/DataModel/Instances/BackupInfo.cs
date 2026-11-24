namespace AxoClient.DataModel.Instances
{
    public sealed record BackupInfo(string File, string World, DateTimeOffset CreatedAt, long SizeBytes);
}
