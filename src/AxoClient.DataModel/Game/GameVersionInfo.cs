namespace AxoClient.DataModel.Game
{
    public sealed record GameVersionInfo(string Id, VersionType Type, DateTimeOffset ReleaseTime);
}
