namespace AxoClient.DataModel.Settings
{
    public sealed class IntegrationSettings
    {
        public bool DiscordRpc { get; set; } = true;
        public DiscordShow DiscordShow { get; set; } = DiscordShow.Always;
        public bool FriendJoinRelay { get; set; } = true;
        public bool TablistIcon { get; set; } = true;
        public bool StreamerMode { get; set; }
    }
}
