using System.Text.RegularExpressions;

namespace AxoClient.Common
{
    /// <summary>
    /// Schwärzt Tokens, UUIDs, IP-Adressen und bekannte Spielernamen. Wird für alle Launcher-Logs und für
    /// jedes "Log kopieren" bzw. "Bericht kopieren" benutzt.
    /// </summary>
    public partial class LogRedactor : ILogRedactor
    {
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _namesLock = new();

        /// <summary>Spielernamen, die beim Kopieren geschwärzt werden (alle angemeldeten Accounts).</summary>
        public void RegisterPlayerName(string name)
        {
            if (name.Length < 3)
            {
                return;
            }

            lock (_namesLock)
            {
                _names.Add(name);
            }
        }

        /// <summary>Immer: Tokens. Für Logs, die die Maschine verlassen könnten.</summary>
        public string Redact(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            text = accessTokenArgumentPattern().Replace(text, "$1 ***");
            text = jsonToken().Replace(text, "$1\"***\"");
            text = jwtPattern().Replace(text, "***");
            text = bearerToken().Replace(text, "Bearer ***");
            return text;
        }

        /// <summary>Zusätzlich zu Tokens: Spielername, UUID und IP-Adressen ("Log kopieren").</summary>
        public string RedactForSharing(string? text)
        {
            text = Redact(text);
            text = uuid().Replace(text, "<uuid>");
            text = ipv4().Replace(text, match => isLocalhost(match.Value) ? match.Value : "<ip>");
            string[] names;
            lock (_namesLock)
            {
                names = _names.ToArray();
            }

            foreach (var name in names.OrderByDescending(name => name.Length))
            {
                text = Regex.Replace(text, $@"(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_])", "<spieler>",
                    RegexOptions.IgnoreCase);
            }
            // Benutzerordner (enthält oft den echten Namen)
            text = userProfilePath().Replace(text, @"$1<benutzer>");
            return text;
        }

        private bool isLocalhost(string address) => address.StartsWith("127.") || address == "0.0.0.0";

        [GeneratedRegex(@"(--accessToken|--session|-Daxoclient\.token=?)\s*\S+", RegexOptions.IgnoreCase)]
        private partial Regex accessTokenArgumentPattern();

        [GeneratedRegex(@"(""(?:access_?token|refresh_?token|token|xuid|id_token)""\s*:\s*)""[^""]*""", RegexOptions.IgnoreCase)]
        private partial Regex jsonToken();

        [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")]
        private partial Regex jwtPattern();

        [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._\-~+/=]{8,}", RegexOptions.IgnoreCase)]
        private partial Regex bearerToken();

        [GeneratedRegex(@"\b[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}\b")]
        private partial Regex uuid();

        [GeneratedRegex(@"\b(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}\b")]
        private partial Regex ipv4();

        [GeneratedRegex(@"([A-Za-z]:\\Users\\)[^\\/\r\n]+", RegexOptions.IgnoreCase)]
        private partial Regex userProfilePath();
    }
}
