namespace AxoClient.DataModel.Social
{
    public sealed record InboxItem(string Id, InboxSender Sender, string Kind, string Title, long Created, long Expires);
}
