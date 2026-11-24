namespace AxoClient.DataModel.Social
{
    /// <summary>Emote eines anderen Spielers (UUID ohne Bindestriche); bei eigenen Emotes mit Animation.</summary>
    public sealed record RemoteEmote(string Uuid, string? Emote, string? Anim, string? Animation);
}
