using System.Windows.Media;
using System.Windows.Threading;

namespace AxoClient.UI;

public enum NoticeKind
{
    Share,
    Invite,
    FriendRequest,
    FriendAccepted,
    FriendOnline,
    AppUpdate,
    ModUpdates,
    Info
}

public sealed class Notice
{
    public required string Key { get; init; }
    public required NoticeKind Kind { get; init; }
    public required string Title { get; init; }
    public string Text { get; init; } = "";
    public string? HeadUrl { get; init; }
    public ImageSource? Image { get; init; }
    public DateTime TimeUtc { get; init; } = DateTime.UtcNow;
    public bool Unread { get; set; }
    public ShareInfo? Share { get; init; }
    public NotificationInfo? Remote { get; init; }
    public UpdateInfo? Update { get; init; }
    public Installation? Instance { get; init; }
    public int Count { get; init; }
}

public sealed class NotificationCenter
{
    private const int MaxSeen = 300;

    private readonly AppServices _app;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly List<Notice> _local = [];
    private List<Notice> _remote = [];
    private bool _loading;
    private bool _firstLoad = true;

    public NotificationCenter(AppServices app)
    {
        _app = app;
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
        _app.Accounts.SignedIn += () => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => _ = RefreshAsync());
        _app.Accounts.SignedOut += () =>
        {
            _remote = [];
            _firstLoad = true;
            Changed?.Invoke();
        };
    }

    public event Action? Changed;
    public event Action<Notice>? Arrived;

    public IReadOnlyList<Notice> Items =>
        _remote.Concat(_local).OrderByDescending(n => n.Unread).ThenByDescending(n => n.TimeUtc).ToList();

    public int UnreadCount => _remote.Count(n => n.Unread) + _local.Count(n => n.Unread);

    public void AddLocal(Notice notice)
    {
        _local.RemoveAll(n => n.Key == notice.Key);
        notice.Unread = !_app.Settings.SeenNotices.Contains(notice.Key);
        _local.Add(notice);
        Changed?.Invoke();
        if (notice.Unread)
            Arrived?.Invoke(notice);
    }

    public void RemoveLocal(string key)
    {
        if (_local.RemoveAll(n => n.Key == key) > 0)
            Changed?.Invoke();
    }

    public async Task RefreshAsync()
    {
        if (_loading || !_app.Axo.Available)
            return;
        _loading = true;
        try
        {
            var notices = new List<Notice>();
            try
            {
                foreach (var share in await _app.Axo.GetInboxAsync())
                    notices.Add(new Notice
                    {
                        Key = "share:" + share.Id,
                        Kind = NoticeKind.Share,
                        Title = $"{share.FromName} hat dir {ShareTitle(share.Kind)} geschickt",
                        Text = share.Title,
                        HeadUrl = share.HeadUrl,
                        TimeUtc = share.CreatedUtc,
                        Share = share
                    });
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Geteilte Sachen abrufen", ex);
            }

            try
            {
                foreach (var remote in await _app.Axo.GetNotificationsAsync())
                    notices.Add(FromRemote(remote));
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Benachrichtigungen abrufen", ex);
            }

            var seen = _app.Settings.SeenNotices;
            foreach (var notice in notices)
                notice.Unread = !seen.Contains(notice.Key) && notice.Remote is not { Read: true };

            var known = _remote.Select(n => n.Key).ToHashSet();
            _remote = notices;
            Changed?.Invoke();
            if (!_firstLoad)
                foreach (var fresh in notices.Where(n => n.Unread && !known.Contains(n.Key)))
                    Arrived?.Invoke(fresh);
            else
                foreach (var fresh in notices.Where(n => n.Unread && n.Kind is NoticeKind.Share or NoticeKind.Invite).Take(1))
                    Arrived?.Invoke(fresh);
            _firstLoad = false;
        }
        finally
        {
            _loading = false;
        }
    }

    public async Task MarkAllReadAsync()
    {
        var seen = _app.Settings.SeenNotices;
        var changed = false;
        foreach (var notice in _remote.Concat(_local).Where(n => n.Unread))
        {
            notice.Unread = false;
            changed |= seen.Add(notice.Key);
        }
        if (seen.Count > MaxSeen)
            seen.RemoveWhere(k => !_remote.Any(n => n.Key == k) && !_local.Any(n => n.Key == k));
        if (changed)
            _app.SaveSettings();
        Changed?.Invoke();
        if (_remote.Any(n => n.Remote is { Read: false }))
        {
            try
            {
                await _app.Axo.MarkNotificationsReadAsync();
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Benachrichtigungen als gelesen markieren", ex);
            }
        }
    }

    public void Forget(Notice notice)
    {
        _remote.Remove(notice);
        _local.Remove(notice);
        Changed?.Invoke();
    }

    private static string ShareTitle(string kind) => kind switch
    {
        ShareKinds.Instance => "eine Instanz",
        ShareKinds.Server => "einen Server",
        ShareKinds.Content => "etwas",
        _ => "etwas"
    };

    private static Notice FromRemote(NotificationInfo remote)
    {
        var from = remote.FromName ?? "Jemand";
        var (kind, title, text) = remote.Kind switch
        {
            "invite" => (NoticeKind.Invite, $"Einladung von {from}",
                $"{from} hat dich auf {remote.Server} eingeladen."),
            "friend-request" => (NoticeKind.FriendRequest, $"{from} möchte dich als Freund hinzufügen",
                "Nimm die Anfrage in der Freundesliste an."),
            "friend-accepted" => (NoticeKind.FriendAccepted, $"{from} hat deine Anfrage angenommen", "Ihr seid jetzt Freunde."),
            "friend-online" => (NoticeKind.FriendOnline, $"{from} ist online",
                remote.Server != null ? $"Spielt gerade auf {remote.Server}." : "Ist gerade im Launcher."),
            _ => (NoticeKind.Info, from, remote.Text)
        };
        return new Notice
        {
            Key = "n:" + remote.Id,
            Kind = kind,
            Title = title,
            Text = string.IsNullOrEmpty(remote.Text) || kind != NoticeKind.Info ? text : remote.Text,
            HeadUrl = remote.HeadUrl,
            TimeUtc = remote.CreatedUtc,
            Remote = remote
        };
    }
}
