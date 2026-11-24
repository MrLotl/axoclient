namespace AxoClient.Common
{
    /// <summary>Inhalt von <c>playtime.json</c> (Instanz) bzw. <c>uptime.json</c> (lokaler Server).</summary>
    public sealed class ActivityLog : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public long TotalSeconds { get; set; }

        /// <summary>Für Instanzen <c>firstPlayed</c>, für Server <c>firstStarted</c> (siehe JSON-Plan).</summary>
        public string? FirstPlayed { get; set; }
        public string? FirstStarted { get; set; }
        public DateTimeOffset? LastPlayed { get; set; }
        public DateTimeOffset? LastStarted { get; set; }
        public long LastSessionSeconds { get; set; }
        public SortedDictionary<string, long> Days { get; set; } = new();
    }
}
