namespace AxoClient.App.Bridge.Requests
{
    public sealed record NameData(string Name);

    public sealed record BlockData(string Uuid, bool Block);

    public sealed record SendInstanceData(string InstanceId, List<string> Recipients, bool Servers = true, bool Options = false, bool ModLock = true);

    public sealed record SendModData(string Title, string Type, string Source, string ProjectId, string VersionId, List<string> Recipients);

    public sealed record SendServerData(string Name, string Address, List<string> Recipients);

    public sealed record InviteData(string Address, string? InstanceName, string? Minecraft, List<string> Recipients);

    public sealed record AcceptData(string Id, string? InstanceId);
}
