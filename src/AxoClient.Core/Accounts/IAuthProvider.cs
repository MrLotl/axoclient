namespace AxoClient.Core.Accounts;

/// <summary>Eine Anmeldeart. Vorerst Microsoft über CmlLib, für die Entwicklung zusätzlich ein Offline-Provider.</summary>
public interface IAuthProvider
{
    AccountType Type { get; }

    /// <summary>Neues Konto interaktiv anmelden (Browser/Anmeldefenster).</summary>
    Task<GameSession> LoginAsync(CancellationToken ct);

    /// <summary>Gespeicherte Anmeldung still erneuern. Wirft <c>auth.expired</c>, wenn der Nutzer neu anmelden muss.</summary>
    Task<GameSession> RefreshAsync(string uuid, CancellationToken ct);

    Task SignOutAsync(string uuid, CancellationToken ct);

    bool HasStoredLogin(string uuid);
}
