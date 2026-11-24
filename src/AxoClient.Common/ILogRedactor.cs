namespace AxoClient.Common
{
    public interface ILogRedactor
    {
        /// <summary>Spielernamen, die beim Kopieren geschwärzt werden (alle angemeldeten Accounts).</summary>
        void RegisterPlayerName(string name);

        /// <summary>Immer: Tokens. Für Logs, die die Maschine verlassen könnten.</summary>
        string Redact(string? text);

        /// <summary>Zusätzlich zu Tokens: Spielername, UUID und IP-Adressen ("Log kopieren").</summary>
        string RedactForSharing(string? text);
    }
}
