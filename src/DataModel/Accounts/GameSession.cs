using System.Text.Json.Serialization;

namespace AxoClient.DataModel.Accounts
{
    /// <summary>Alles, was zum Starten des Spiels nötig ist. Wird nie gespeichert oder an die UI geschickt.</summary>
    public sealed record GameSession(string Uuid, string Name, string AccessToken, string? Xuid, AccountType Type)
    {
        [JsonIgnore]
        public string CompactUuid => Uuid.Replace("-", "").ToLowerInvariant();
    }
}
