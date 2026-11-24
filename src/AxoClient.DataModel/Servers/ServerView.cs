using AxoClient.Common;

namespace AxoClient.DataModel.Servers
{
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
}
