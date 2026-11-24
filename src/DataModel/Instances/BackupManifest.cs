using AxoClient.Common;

namespace AxoClient.DataModel.Instances
{
    /// <summary>Manifest im Backup-ZIP (<c>axoclient-backup.json</c>).</summary>
    public sealed class BackupManifest : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public string App { get; set; } = "AxoClient";
        public string AppVersion { get; set; } = "";
        public string InstanceName { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
