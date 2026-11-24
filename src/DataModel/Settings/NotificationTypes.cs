namespace AxoClient.DataModel.Settings
{
    public sealed class NotificationTypes
    {
        public bool FriendRequest { get; set; } = true;
        public bool FriendAccepted { get; set; } = true;
        public bool FriendOnline { get; set; }
        public bool FriendOnServer { get; set; } = true;
        public bool FriendOpenWorld { get; set; } = true;
        public bool Invite { get; set; } = true;
        public bool Inbox { get; set; } = true;
        public bool ModUpdates { get; set; } = true;
        public bool ModpackUpdates { get; set; } = true;
        public bool CapeReview { get; set; } = true;
        public bool LauncherUpdate { get; set; } = true;
        public bool JobFinished { get; set; } = true;
        public bool AuthExpired { get; set; } = true;
        public bool LowDiskSpace { get; set; } = true;
        public bool ServerCrashed { get; set; } = true;
        public bool ServerPlayerJoined { get; set; }

        public bool IsEnabled(string type) => type switch
        {
            "friendRequest" => FriendRequest,
            "friendAccepted" => FriendAccepted,
            "friendOnline" => FriendOnline,
            "friendOnServer" => FriendOnServer,
            "friendOpenWorld" => FriendOpenWorld,
            "invite" => Invite,
            "inbox" => Inbox,
            "modUpdates" => ModUpdates,
            "modpackUpdates" => ModpackUpdates,
            "capeReview" => CapeReview,
            "launcherUpdate" => LauncherUpdate,
            "jobFinished" => JobFinished,
            "authExpired" => AuthExpired,
            "lowDiskSpace" => LowDiskSpace,
            "serverCrashed" => ServerCrashed,
            "serverPlayerJoined" => ServerPlayerJoined,
            _ => true
        };
    }
}
