namespace AxoClient.DataModel.Skins
{
    public sealed class CapeItem : ILibraryItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string File { get; set; } = "";

        /// <summary>Auflösung der Vorderseite im Editor, z. B. "20x32".</summary>
        public string Resolution { get; set; } = "10x16";
        public CapeSource Source { get; set; } = CapeSource.Editor;

        /// <summary>Nur lokal, eingereicht, freigegeben oder abgelehnt.</summary>
        public CapeStatus Status { get; set; } = CapeStatus.Local;
        public string? Reason { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
