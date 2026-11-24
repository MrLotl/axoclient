using AxoClient.Common;
using AxoClient.DataModel.Social;

namespace AxoClient.Social.Contract
{
    public interface INotificationManager
    {
        /// <summary>Wird von der App gesetzt: Ist der Launcher minimiert/ausgeblendet? Läuft Minecraft?</summary>
        Func<bool> IsInBackground { get; set; }

        Func<bool> IsPlaying { get; set; }

        /// <summary>Windows-Benachrichtigung anzeigen (von der App gesetzt).</summary>
        Action<NotificationItem>? ShowToast { get; set; }

        IReadOnlyList<NotificationItem> All { get; }

        int UnreadCount { get; }

        /// <summary>Lokale Benachrichtigung hinzufügen (z. B. Mod-Updates, Launcher-Update).</summary>
        void Add(string type, TextRef text, Dictionary<string, object?>? data = null, string? dedupeKey = null);

        /// <summary>Neue Liste vom Service übernehmen; neue Einträge melden.</summary>
        void ApplyRemote(IEnumerable<ServiceNotification> remote);

        void MarkAllRead();

        /// <summary>Lokale Benachrichtigungen zu einer gelöschten Instanz entfernen (z. B. Mod-Updates).</summary>
        void RemoveForInstance(string instanceId);

        void Remove(string id);
    }
}
