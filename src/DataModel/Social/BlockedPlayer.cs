namespace AxoClient.DataModel.Social
{
    /// <summary>Ein Spieler auf der eigenen Blockliste (<c>GET /v2/blocks</c>), <c>Created</c> in Unix-Millisekunden.</summary>
    public sealed record BlockedPlayer(string Uuid, string Name, long Created);
}
