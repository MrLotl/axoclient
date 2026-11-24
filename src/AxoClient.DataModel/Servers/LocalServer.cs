using AxoClient.Common;

namespace AxoClient.DataModel.Servers
{
    /// <summary><c>servers/&lt;id&gt;/server.json</c></summary>
    public sealed class LocalServer : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ServerSoftware Software { get; set; } = ServerSoftware.Paper;
        public string Version { get; set; } = "";
        public int Port { get; set; } = 25565;
        public int MaxPlayers { get; set; } = 10;
        public int MemoryMb { get; set; } = 4096;
        public bool OpenToInternet { get; set; }
        public bool EulaAccepted { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Welche Jar zuletzt installiert wurde (Software und Version ändern sich erst beim nächsten Start).</summary>
        public string? InstalledJar { get; set; }
        public string? InstalledFor { get; set; }
    }
}
