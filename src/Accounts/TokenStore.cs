using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AxoClient.Accounts.Contract;
using AxoClient.Common;

namespace AxoClient.Accounts
{
    /// <summary>
    /// Alle Tokens (Microsoft-/Xbox-Sitzungen, Online-Service) liegen per DPAPI verschlüsselt in <c>tokens.dat</c>.
    /// Nur der angemeldete Windows-Benutzer kann sie entschlüsseln, nie im Klartext in einer JSON-Datei.
    /// </summary>
    public sealed class TokenStore : ITokenStore
    {
        private readonly IFileSystem _fs;
        private readonly string _path;
        private readonly IAppLog _appLog;
        private readonly byte[] _entropy = "AxoClient.TokenStore.v1"u8.ToArray();

        private readonly Lock _lock = new();
        private JsonObject? _data;

        public TokenStore(IFileSystem fs, string path, IAppLog appLog)
        {
            _fs = fs;
            _path = path;
            _appLog = appLog;
            Protect = data => ProtectedData.Protect(data, _entropy, DataProtectionScope.CurrentUser);
            Unprotect = data => ProtectedData.Unprotect(data, _entropy, DataProtectionScope.CurrentUser);
        }

        /// <summary>Für Tests ohne DPAPI.</summary>
        internal Func<byte[], byte[]> Protect { get; set; }

        internal Func<byte[], byte[]> Unprotect { get; set; }

        public JsonNode? Get(string key)
        {
            lock (_lock)
            {
                return load()[key]?.DeepClone();
            }
        }

        public string? GetString(string key) => Get(key)?.GetValue<string>();

        public void Set(string key, JsonNode? value)
        {
            lock (_lock)
            {
                var data = load();
                data[key] = value?.DeepClone();
                save(data);
            }
        }

        public void Remove(string key)
        {
            lock (_lock)
            {
                var data = load();
                if (data.Remove(key))
                {
                    save(data);
                }
            }
        }

        public void RemoveWhere(Func<string, bool> predicate)
        {
            lock (_lock)
            {
                var data = load();
                foreach (var key in data.Select(p => p.Key).Where(predicate).ToList())
                {
                    data.Remove(key);
                }

                save(data);
            }
        }

        private JsonObject load()
        {
            if (_data != null)
            {
                return _data;
            }

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
                _appLog.Warn("Gespeicherte Anmeldungen konnten nicht gelesen werden", ex);
            }
            return _data ??= new JsonObject();
        }

        private void save(JsonObject data)
        {
            var dir = _fs.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                _fs.Directory.CreateDirectory(dir);
            }

            var tmp = _path + ".tmp";
            _fs.File.WriteAllBytes(tmp, Protect(Encoding.UTF8.GetBytes(data.ToJsonString())));
            _fs.File.Move(tmp, _path, overwrite: true);
        }
    }
}
