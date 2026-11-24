using System.Text.Json.Nodes;

namespace AxoClient.Common
{
    /// <summary>Eine Migration hebt ein Dokument von <see cref="From"/> auf die nächste Formatversion.</summary>
    public sealed record JsonMigration(int From, Action<JsonObject> Apply);
}
