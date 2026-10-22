using AxoClient.Core.Common;

namespace AxoClient.Core.Social;

/// <summary>
/// Die Glocke: Benachrichtigungen vom Service und lokale (Updates, Abstürze …). Entscheidet anhand der Einstellungen,
/// ob zusätzlich eine Windows-Benachrichtigung erscheint.
/// </summary>
public sealed class NotificationManager(SettingsService settings, EventHub events)
{
    private readonly List<NotificationItem> _local = [];
    private readonly Lock _lock = new();
    private List<NotificationItem> _remote = [];
    private readonly HashSet<string> _known = [];

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
                return _local.Concat(_remote).OrderByDescending(n => n.Created).Take(100).ToList();
        }
    }

    public int UnreadCount
    {
        get
        {
            lock (_lock)
                return _local.Count(n => !n.Read) + _remote.Count(n => !n.Read);
        }
    }

    /// <summary>Lokale Benachrichtigung hinzufügen (z. B. Mod-Updates, Launcher-Update).</summary>
    public void Add(string type, TextRef text, Dictionary<string, object?>? data = null, string? dedupeKey = null)
    {
        var types = settings.Current.Notifications.Types;
        if (!types.IsEnabled(type))
            return;
        var item = new NotificationItem(dedupeKey ?? Guid.NewGuid().ToString("N")[..12], type, text, DateTimeOffset.Now, false, data);
        lock (_lock)
        {
            if (dedupeKey != null)
                _local.RemoveAll(n => n.Id == dedupeKey);
            _local.Add(item);
            if (_local.Count > 100)
                _local.RemoveAt(0);
        }
        Publish(item);
    }

    /// <summary>Neue Liste vom Service übernehmen; neue Einträge melden.</summary>
    public void ApplyRemote(IEnumerable<ServiceNotification> remote)
    {
        var fresh = new List<NotificationItem>();
        lock (_lock)
        {
            _remote = remote.Select(r =>
            {
                var item = new NotificationItem("s:" + r.Id, r.Kind, new TextRef("notifications." + r.Kind, r.Data),
                    DateTimeOffset.FromUnixTimeMilliseconds(r.Created), r.Read, r.Data, true);
                if (_known.Add(item.Id) && !r.Read)
                    fresh.Add(item);
                return item;
            }).Where(n => settings.Current.Notifications.Types.IsEnabled(n.Type)).ToList();
        }
        foreach (var item in fresh)
            Publish(item);
        if (fresh.Count == 0)
            events.Publish("notifications.changed", new { unread = UnreadCount });
    }

    public void MarkAllRead()
    {
        lock (_lock)
        {
            for (var i = 0; i < _local.Count; i++)
                _local[i] = _local[i] with { Read = true };
            _remote = _remote.Select(n => n with { Read = true }).ToList();
        }
        events.Publish("notifications.changed", new { unread = 0 });
    }

    /// <summary>Lokale Benachrichtigungen zu einer gelöschten Instanz entfernen (z. B. Mod-Updates).</summary>
    public void RemoveForInstance(string instanceId)
    {
        lock (_lock)
            _local.RemoveAll(n => n.Data?.GetValueOrDefault("instanceId") is string id && id == instanceId);
        events.Publish("notifications.changed", new { unread = UnreadCount });
    }

    public void Remove(string id)
    {
        lock (_lock)
        {
            _local.RemoveAll(n => n.Id == id);
            _remote.RemoveAll(n => n.Id == id);
        }
        events.Publish("notifications.changed", new { unread = UnreadCount });
    }

    private void Publish(NotificationItem item)
    {
        events.Publish("notification.new", item);
        events.Publish("notifications.changed", new { unread = UnreadCount });
        var n = settings.Current.Notifications;
        if (!n.WindowsToasts || !IsInBackground())
            return;
        if (n.QuietWhilePlaying && IsPlaying() && !(item.Type == "invite" && n.InvitesWhilePlaying))
            return;
        ShowToast?.Invoke(item);
    }
}
