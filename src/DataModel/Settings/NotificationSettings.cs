namespace AxoClient.DataModel.Settings
{
    public sealed class NotificationSettings
    {
        public bool WindowsToasts { get; set; } = true;
        public bool QuietWhilePlaying { get; set; } = true;
        public bool InvitesWhilePlaying { get; set; } = true;
        public FriendOnServerScope FriendOnServerScope { get; set; } = FriendOnServerScope.Active;
        public NotificationTypes Types { get; set; } = new();
    }
}
