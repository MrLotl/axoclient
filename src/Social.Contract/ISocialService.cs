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
        Task SyncNow(CancellationToken cancellationToken = default);

        /// <summary>Wo man gerade spielt (Server-Adresse oder e4mc-Adresse der offenen Welt), damit Freunde nachjoinen können.</summary>
        void SetJoinAddress(string? address);

        Task SendRequest(string name, CancellationToken cancellationToken);

        Task Accept(string uuid, CancellationToken cancellationToken);

        /// <summary>Anfrage zurückziehen oder ablehnen.</summary>
        Task Decline(string uuid, CancellationToken cancellationToken);

        Task Remove(string uuid, CancellationToken cancellationToken);

        Task Block(string uuid, bool block, CancellationToken cancellationToken);

        /// <summary>Eigene Blockliste vom Service holen; meldet sie als <c>social.blocked</c>.</summary>
        Task<IReadOnlyList<BlockedPlayer>> LoadBlocked(CancellationToken cancellationToken);

        Task MarkNotificationsRead(CancellationToken cancellationToken);

        Task DeleteAccount(CancellationToken cancellationToken);
    }
}
