using AxoClient.DataModel.Accounts;

namespace AxoClient.Accounts.Contract
{
    public interface IAccountManager
    {
        IReadOnlyList<AccountEntry> Accounts { get; }

        AccountEntry? Active { get; }

        /// <summary>Ist die aktive Anmeldung kaputt (z. B. abgelaufen), damit die UI "Willkommen zurück" zeigt.</summary>
        bool ActiveNeedsLogin { get; }

        event Action? Changed;

        Task Load();

        IReadOnlyList<AccountView> Views();

        /// <summary>Neues Konto hinzufügen (oder ein bestehendes neu anmelden) und aktiv setzen.</summary>
        Task<AccountEntry> Login(AccountType type, CancellationToken ct);

        Task Switch(string uuid, CancellationToken ct);

        Task Remove(string uuid, CancellationToken ct);

        /// <summary>Gültige Sitzung für den Spielstart. Erneuert still, wenn sie älter als 10 Minuten ist.</summary>
        Task<GameSession> GetSession(CancellationToken ct, bool forceRefresh = false);

        /// <summary>Für Skin/Cape-Aufrufe an Mojang, ohne das Token herauszugeben.</summary>
        GameSession? CachedSession { get; }

        Task UpdateEntry(string uuid, Action<AccountEntry> change);
    }
}
