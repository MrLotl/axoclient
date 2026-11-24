using AxoClient.Common;
using AxoClient.DataModel.Social;
using AxoClient.Settings.Contract;
using AxoClient.Social.Contract;

namespace AxoClient.Social
{
    /// <summary>
    /// Die Glocke: Benachrichtigungen vom Service und lokale (Updates, Abstürze …). Entscheidet anhand der Einstellungen,
    /// ob zusätzlich eine Windows-Benachrichtigung erscheint.
    /// </summary>
    public sealed class NotificationManager : INotificationManager
    {
        private readonly ISettingsService _settings;
        private readonly IEventHub _events;
        private readonly List<NotificationItem> _local = [];
        private readonly Lock _lock = new();
        private List<NotificationItem> _remote = [];
        private readonly HashSet<string> _known = [];

        public NotificationManager(ISettingsService settings, IEventHub events)
        {
            _settings = settings;
            _events = events;
        }

        /// <summary>Wird von der App gesetzt: Ist der Launcher minimiert/ausgeblendet? Läuft Minecraft?</summary>
        public Func<bool> IsInBackground { get; set; } = () => false;
        public Func<bool> IsPlaying { get; set; } = () => false;

        /// <summary>Windows-Benachrichtigung anzeigen (von der App gesetzt).</summary>
        public Action<NotificationItem>? ShowToast { get; set; }

        public IReadOnlyList<NotificationItem> All
        {
            get
            {
                lock (_lock)
                {
                    return _local.Concat(_remote).OrderByDescending(notification => notification.Created).Take(100).ToList();
                }
            }
        }

        public int UnreadCount
        {
            get
            {
                lock (_lock)
                {
                    return _local.Count(notification => !notification.Read) + _remote.Count(notification => !notification.Read);
                }
            }
        }

        /// <summary>Lokale Benachrichtigung hinzufügen (z. B. Mod-Updates, Launcher-Update).</summary>
        public void Add(string type, TextRef text, Dictionary<string, object?>? data = null, string? dedupeKey = null)
        {
            var types = _settings.Current.Notifications.Types;
            if (!types.IsEnabled(type))
            {
                return;
            }

            var item = new NotificationItem(dedupeKey ?? Guid.NewGuid().ToString("N")[..12], type, text, DateTimeOffset.Now, false, data);
            lock (_lock)
            {
                if (dedupeKey != null)
                {
                    _local.RemoveAll(notification => notification.Id == dedupeKey);
                }

                _local.Add(item);
                if (_local.Count > 100)
                {
                    _local.RemoveAt(0);
                }
            }
            publish(item);
        }

        /// <summary>Neue Liste vom Service übernehmen; neue Einträge melden.</summary>
        public void ApplyRemote(IEnumerable<ServiceNotification> remote)
        {
            var fresh = new List<NotificationItem>();
            lock (_lock)
            {
                _remote = remote.Select(notification =>
                {
                    var item = new NotificationItem("s:" + notification.Id, notification.Kind, new TextRef("notifications." + notification.Kind, notification.Data),
                        DateTimeOffset.FromUnixTimeMilliseconds(notification.Created), notification.Read, notification.Data, true);
                    if (_known.Add(item.Id) && !notification.Read)
                    {
                        fresh.Add(item);
                    }

                    return item;
                }).Where(notification => _settings.Current.Notifications.Types.IsEnabled(notification.Type)).ToList();
            }
            foreach (var item in fresh)
            {
                publish(item);
            }

            if (fresh.Count == 0)
            {
                _events.Publish("notifications.changed", new { unread = UnreadCount });
            }
        }

        public void MarkAllRead()
        {
            lock (_lock)
            {
                for (var index = 0; index < _local.Count; index++)
                {
                    _local[index] = _local[index] with { Read = true };
                }

                _remote = _remote.Select(notification => notification with { Read = true }).ToList();
            }
            _events.Publish("notifications.changed", new { unread = 0 });
        }

        /// <summary>Lokale Benachrichtigungen zu einer gelöschten Instanz entfernen (z. B. Mod-Updates).</summary>
        public void RemoveForInstance(string instanceId)
        {
            lock (_lock)
            {
                _local.RemoveAll(notification => notification.Data?.GetValueOrDefault("instanceId") is string id && id == instanceId);
            }

            _events.Publish("notifications.changed", new { unread = UnreadCount });
        }

        public void Remove(string id)
        {
            lock (_lock)
            {
                _local.RemoveAll(notification => notification.Id == id);
                _remote.RemoveAll(notification => notification.Id == id);
            }
            _events.Publish("notifications.changed", new { unread = UnreadCount });
        }

        private void publish(NotificationItem item)
        {
            _events.Publish("notification.new", item);
            _events.Publish("notifications.changed", new { unread = UnreadCount });
            var notifications = _settings.Current.Notifications;
            if (!notifications.WindowsToasts || !IsInBackground())
            {
                return;
            }

            if (notifications.QuietWhilePlaying && IsPlaying() && !(item.Type == "invite" && notifications.InvitesWhilePlaying))
            {
                return;
            }

            ShowToast?.Invoke(item);
        }
    }
}
