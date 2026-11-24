namespace AxoClient.DataModel.Instances
{
    public sealed class InstanceOrigin
    {
        public OriginType Type { get; set; } = OriginType.Empty;

        /// <summary>Bei Modpacks: Quelle und Projekt, damit Modpack-Updates gefunden werden.</summary>
        public string? Source { get; set; }
        public string? ProjectId { get; set; }
        public string? VersionId { get; set; }
        public string? VersionName { get; set; }

        /// <summary>Bei Kopien: von welcher Instanz.</summary>
        public string? CopiedFrom { get; set; }
    }
}
