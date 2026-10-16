using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AxoClient.Core.Common;

namespace AxoClient.Core.Accounts;

/// <summary>
/// Alle Tokens (Microsoft-/Xbox-Sitzungen, Online-Service) liegen per DPAPI verschlüsselt in <c>tokens.dat</c>.
/// Nur der angemeldete Windows-Benutzer kann sie entschlüsseln, nie im Klartext in einer JSON-Datei.
/// </summary>
public sealed class TokenStore
{
    private static readonly byte[] Entropy = "AxoClient.TokenStore.v1"u8.ToArray();

    private readonly IFileSystem _fs;
    private readonly string _path;
    private readonly Lock _lock = new();
    private JsonObject? _data;

    public TokenStore(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    /// <summary>Für Tests ohne DPAPI.</summary>
    internal Func<byte[], byte[]> Protect { get; set; } =
        data => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    internal Func<byte[], byte[]> Unprotect { get; set; } =
        data => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);

    public JsonNode? Get(string key)
    {
        lock (_lock)
            return Load()[key]?.DeepClone();
    }

    public string? GetString(string key) => Get(key)?.GetValue<string>();

    public void Set(string key, JsonNode? value)
    {
        lock (_lock)
        {
            var data = Load();
            data[key] = value?.DeepClone();
            Save(data);
        }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            var data = Load();
            if (data.Remove(key))
                Save(data);
        }
    }

    public void RemoveWhere(Func<string, bool> predicate)
    {
        lock (_lock)
        {
            var data = Load();
            foreach (var key in data.Select(p => p.Key).Where(predicate).ToList())
                data.Remove(key);
            Save(data);
        }
    }

    private JsonObject Load()
    {
        if (_data != null)
            return _data;
        try
        {
            if (_fs.File.Exists(_path))
            {
                var plain = Unprotect(_fs.File.ReadAllBytes(_path));
                _data = JsonNode.Parse(Encoding.UTF8.GetString(plain)) as JsonObject;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or System.Text.Json.JsonException or IOException)
        {
            // Anderer Windows-Benutzer oder beschädigt: alle müssen sich einmal neu anmelden
            AppLog.Warn("Gespeicherte Anmeldungen konnten nicht gelesen werden", ex);
        }
        return _data ??= new JsonObject();
    }

    private void Save(JsonObject data)
    {
        var dir = _fs.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            _fs.Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        _fs.File.WriteAllBytes(tmp, Protect(Encoding.UTF8.GetBytes(data.ToJsonString())));
        _fs.File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>Bindet CmlLibs Account-Speicher an den verschlüsselten <see cref="TokenStore"/>.</summary>
internal sealed class TokenStoreJsonStorage(TokenStore store, string key) : XboxAuthNet.Game.Accounts.JsonStorage.IJsonStorage
{
    public JsonNode ReadAsJsonNode() => store.Get(key) ?? new JsonObject();

    public void Write(JsonNode node, System.Text.Json.JsonSerializerOptions? serializerOptions) => store.Set(key, node);
}
