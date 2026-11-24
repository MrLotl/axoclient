using System.Text.Json.Nodes;

namespace AxoClient.Accounts.Contract
{
    public interface ITokenStore
    {
        JsonNode? Get(string key);

        string? GetString(string key);

        void Set(string key, JsonNode? value);

        void Remove(string key);

        void RemoveWhere(Func<string, bool> predicate);
    }
}
