namespace AxoClient.DataModel.Social
{
    public sealed record ServiceNotification(string Id, string Kind, Dictionary<string, object?>? Data, long Created, bool Read);
}
