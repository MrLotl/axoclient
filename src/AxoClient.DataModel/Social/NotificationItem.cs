using AxoClient.Common;

namespace AxoClient.DataModel.Social
{
    /// <summary>Ein Eintrag in der Glocke (vom Service oder lokal entstanden).</summary>
    public sealed record NotificationItem(
        string Id,
        string Type,
        TextRef Text,
        DateTimeOffset Created,
        bool Read,
        Dictionary<string, object?>? Data = null,
        bool FromService = false);
}
