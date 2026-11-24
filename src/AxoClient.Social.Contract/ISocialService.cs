using AxoClient.DataModel.Social;
using AxoClient.Launch.Contract;

namespace AxoClient.Social.Contract
{
    public interface ISocialService : IGameLaunchHook
    {
        /// <summary>Jede Benachrichtigung vom Dienst einmal (z. B. Ergebnis einer Cape-Prüfung).</summary>
        event Action<ServiceNotification>? RemoteNotification;

        Me? Me { get; }

        IReadOnlyList<Friend> Friends { get; }

        IReadOnlyList<InboxItem> Inbox { get; }

        /// <summary>Zuletzt geladene Blockliste (leer, bis <see cref="LoadBlocked"/> lief).</summary>
        IReadOnlyList<BlockedPlayer> Blocked { get; }

        bool Available { get; }

        void Start();

        void Stop();

        /// <summary>Sofort synchronisieren (z. B. nach dem Senden einer Anfrage).</summary>
        Task SyncNow(CancellationToken ct = default);

        /// <summary>Wo man gerade spielt (Server-Adresse oder e4mc-Adresse der offenen Welt), damit Freunde nachjoinen können.</summary>
        void SetJoinAddress(string? address);

        Task SendRequest(string name, CancellationToken ct);

        Task Accept(string uuid, CancellationToken ct);

        /// <summary>Anfrage zurückziehen oder ablehnen.</summary>
        Task Decline(string uuid, CancellationToken ct);

        Task Remove(string uuid, CancellationToken ct);

        Task Block(string uuid, bool block, CancellationToken ct);

        /// <summary>Eigene Blockliste vom Service holen; meldet sie als <c>social.blocked</c>.</summary>
        Task<IReadOnlyList<BlockedPlayer>> LoadBlocked(CancellationToken ct);

        Task MarkNotificationsRead(CancellationToken ct);

        Task DeleteAccount(CancellationToken ct);
    }
}
