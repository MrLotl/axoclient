namespace AxoClient.DataModel.Social
{
    public sealed record EmoteView(string Id, string Name, string Source, DateTimeOffset CreatedAt, bool Loop, bool Moving);
}
