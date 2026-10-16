using System.Text.RegularExpressions;

namespace AxoClient.Core.Common;

/// <summary>
/// Schwärzt Tokens, UUIDs, IP-Adressen und bekannte Spielernamen. Wird für alle Launcher-Logs und für
/// jedes "Log kopieren" bzw. "Bericht kopieren" benutzt.
/// </summary>
public static partial class LogRedactor
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock NamesLock = new();

    /// <summary>Spielernamen, die beim Kopieren geschwärzt werden (alle angemeldeten Accounts).</summary>
    public static void RegisterPlayerName(string name)
    {
        if (name.Length < 3)
            return;
        lock (NamesLock)
            Names.Add(name);
    }

    /// <summary>Immer: Tokens. Für Logs, die die Maschine verlassen könnten.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        text = AccessTokenArg().Replace(text, "$1 ***");
        text = JsonToken().Replace(text, "$1\"***\"");
        text = Jwt().Replace(text, "***");
        text = BearerToken().Replace(text, "Bearer ***");
        return text;
    }

    /// <summary>Zusätzlich zu Tokens: Spielername, UUID und IP-Adressen ("Log kopieren").</summary>
    public static string RedactForSharing(string? text)
    {
        text = Redact(text);
        text = Uuid().Replace(text, "<uuid>");
        text = Ipv4().Replace(text, m => IsLocalhost(m.Value) ? m.Value : "<ip>");
        string[] names;
        lock (NamesLock)
            names = Names.ToArray();
        foreach (var name in names.OrderByDescending(n => n.Length))
            text = Regex.Replace(text, $@"(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_])", "<spieler>",
                RegexOptions.IgnoreCase);
        // Benutzerordner (enthält oft den echten Namen)
        text = UserProfilePath().Replace(text, @"$1<benutzer>");
        return text;
    }

    private static bool IsLocalhost(string ip) => ip.StartsWith("127.") || ip == "0.0.0.0";

    [GeneratedRegex(@"(--accessToken|--session|-Daxoclient\.token=?)\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenArg();

    [GeneratedRegex(@"(""(?:access_?token|refresh_?token|token|xuid|id_token)""\s*:\s*)""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonToken();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._\-~+/=]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}\b")]
    private static partial Regex Uuid();

    [GeneratedRegex(@"\b(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}\b")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"([A-Za-z]:\\Users\\)[^\\/\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex UserProfilePath();
}
