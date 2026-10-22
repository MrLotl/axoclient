using AxoClient.Core.Common;

namespace AxoClient.Core.Servers;

public enum ServerSoftware
{
    Vanilla,
    Paper,
    Fabric,
    Velocity
}

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

public sealed record ServerView(
    string Id,
    string Name,
    ServerSoftware Software,
    string Version,
    int Port,
    int MaxPlayers,
    int MemoryMb,
    bool OpenToInternet,
    bool EulaAccepted,
    bool Running,
    int OnlinePlayers,
    IReadOnlyList<string> Players,
    long? MemoryUsedMb,
    DateTimeOffset? StartedAt,
    string? PublicAddress,
    TextRef? InternetProblem,
    string IconUrl,
    string? BannerUrl,
    int? ModCount,
    long TotalUptimeSeconds);

public sealed record ServerRequest(
    string Name,
    ServerSoftware Software,
    string Version,
    int Port,
    int MaxPlayers,
    int MemoryMb,
    bool OpenToInternet,
    bool EulaAccepted);

public sealed record ServerVersionInfo(string Version, bool Stable);
