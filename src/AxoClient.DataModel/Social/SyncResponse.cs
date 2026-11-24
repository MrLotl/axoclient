namespace AxoClient.DataModel.Social
{
    /// <summary>Antwort von <c>GET /v2/sync</c>: alles in einem Aufruf.</summary>
    public sealed record SyncResponse(Me Me, List<Friend> Friends, List<ServiceNotification> Notifications, List<InboxItem> Inbox);
}
