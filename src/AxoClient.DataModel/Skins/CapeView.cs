namespace AxoClient.DataModel.Skins
{
    public sealed record CapeView(string Id, string Name, string Resolution, CapeSource Source, CapeStatus Status, string? Reason, string Url,
        bool Active, bool HasEditState);
}
