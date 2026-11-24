using System.Text.Json.Nodes;
using AxoClient.Accounts.Contract;

namespace AxoClient.Accounts
{
    /// <summary>Bindet CmlLibs Account-Speicher an den verschlüsselten <see cref="TokenStore"/>.</summary>
    internal sealed class TokenStoreJsonStorage : XboxAuthNet.Game.Accounts.JsonStorage.IJsonStorage
    {
        private readonly ITokenStore _store;
        private readonly string _key;

        public TokenStoreJsonStorage(ITokenStore store, string key)
        {
            _store = store;
            _key = key;
        }

        public JsonNode ReadAsJsonNode() => _store.Get(_key) ?? new JsonObject();

        public void Write(JsonNode node, System.Text.Json.JsonSerializerOptions? serializerOptions) => _store.Set(_key, node);
    }
}
