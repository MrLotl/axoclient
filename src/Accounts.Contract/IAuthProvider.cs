using AxoClient.DataModel.Accounts;

namespace AxoClient.Accounts.Contract
{
    /// <summary>Eine Anmeldeart. Vorerst Microsoft über CmlLib, für die Entwicklung zusätzlich ein Offline-Provider.</summary>
    public interface IAuthProvider
    {
        AccountType Type { get; }

        /// <summary>Neues Konto interaktiv anmelden (Browser/Anmeldefenster).</summary>
        Task<GameSession> Login(CancellationToken cancellationToken);

        /// <summary>Gespeicherte Anmeldung still erneuern. Wirft <c>auth.expired</c>, wenn der Nutzer neu anmelden muss.</summary>
        Task<GameSession> Refresh(string uuid, CancellationToken cancellationToken);

        Task SignOut(string uuid, CancellationToken cancellationToken);

        bool HasStoredLogin(string uuid);
    }
}
