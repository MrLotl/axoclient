namespace AxoClient.DataModel.Diagnostics
{
    public sealed record StorageOverview(
        IReadOnlyList<InstanceStorage> Instances,
        long Backups,
        long Game,
        long Java,
        long Cache,
        long Logs,
        long Servers,
        long Total);
}
