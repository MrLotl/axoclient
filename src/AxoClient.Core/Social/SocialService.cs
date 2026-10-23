using AxoClient.Core.Common;
using AxoClient.Core.Instances;
using AxoClient.Core.Launch;

namespace AxoClient.Core.Social;

/// <summary>
/// Freunde und Status. Ein <c>GET /v2/sync</c> alle 30–60 Sekunden statt vieler Einzelabfragen, danach der Heartbeat.
/// Ist der Service nicht erreichbar, heißt es "nicht verfügbar" und es wird später erneut versucht, ohne Dialog.
/// </summary>
public sealed class SocialService(
    OnlineClient client,
    NotificationManager notifications,
    SettingsService settings,
    GameLauncher games,
    InstanceManager instances,
    ServerListManager serverLists,
    EventHub events) : IGameLaunchHook
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(45);
    private readonly HashSet<string> _onServerNotified = [];
    private Dictionary<string, Friend> _friends = new();
    private CancellationTokenSource? _loop;
    private PresenceState _state = PresenceState.Launcher;
    private long _since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private string? _join;
    private string? _activeInstance;

    private readonly HashSet<string> _seenRemote = [];

    /// <summary>Jede Benachrichtigung vom Dienst einmal (z. B. Ergebnis einer Cape-Prüfung).</summary>
    public event Action<ServiceNotification>? RemoteNotification;

    public Me? Me { get; private set; }
    public IReadOnlyList<Friend> Friends => _friends.Values.OrderBy(f => f.Relation).ThenByDescending(f => f.State)
        .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    public IReadOnlyList<InboxItem> Inbox { get; private set; } = [];

    /// <summary>Zuletzt geladene Blockliste (leer, bis <see cref="LoadBlockedAsync"/> lief).</summary>
    public IReadOnlyList<BlockedPlayer> Blocked { get; private set; } = [];
    public bool Available => client.Available && client.CanUse;

    public void Start()
    {
        Stop();
        _loop = new CancellationTokenSource();
        _ = LoopAsync(_loop.Token);
    }

    public void Stop()
    {
        _loop?.Cancel();
        _loop = null;
    }

    /// <summary>Sofort synchronisieren (z. B. nach dem Senden einer Anfrage).</summary>
    public async Task SyncNowAsync(CancellationToken ct = default)
    {
        if (!client.CanUse)
            return;
        var sync = await client.SendAsync<SyncResponse>(HttpMethod.Get, "/sync", ct: ct);
        if (sync == null)
            return;
        Me = sync.Me;
        var previous = _friends;
        _friends = sync.Friends.ToDictionary(f => f.Uuid);
        Inbox = sync.Inbox;
        notifications.ApplyRemote(sync.Notifications);
        foreach (var n in sync.Notifications.Where(n => _seenRemote.Add(n.Id)))
            RemoteNotification?.Invoke(n);
        DetectLocalNotifications(previous);
        events.Publish("social.changed", new { friends = Friends, inbox = Inbox, me = Me, available = true });
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SyncNowAsync(ct);
                await HeartbeatAsync(ct);
            }
            catch (AxoException ex) when (ex.Code is "service.unavailable" or "service.notSignedIn")
            {
                events.Publish("social.changed", new { friends = Array.Empty<Friend>(), inbox = Array.Empty<InboxItem>(), me = (Me?)null, available = false });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Warn("Abgleich mit dem Online-Service fehlgeschlagen", ex);
            }
            try
            {
                await Task.Delay(Interval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        var social = settings.Current.Social;
        if (!social.ShowOnline)
        {
            await client.SendAsync(HttpMethod.Put, "/status", new { state = "offline", since = _since, join = (string?)null }, ct);
            return;
        }
        await client.SendAsync(HttpMethod.Put, "/status", new
        {
            state = _state.ToString().ToLowerInvariant(),
            since = _since,
            join = social.AllowJoin && !settings.Current.Integrations.StreamerMode ? _join : null
        }, ct);
    }

    /// <summary>Wo man gerade spielt (Server-Adresse oder e4mc-Adresse der offenen Welt), damit Freunde nachjoinen können.</summary>
    public void SetJoinAddress(string? address)
    {
        _join = address != null && ServerPing.IsValidAddress(address) ? address : null;
        _ = HeartbeatSafeAsync();
    }

    void IGameLaunchHook.OnStarted(LaunchContext ctx, RunningProcess process)
    {
        _state = PresenceState.Ingame;
        _since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _activeInstance = ctx.Instance.Id;
        _join = ctx.QuickPlay?.Server;
        _onServerNotified.Clear();
        _ = HeartbeatSafeAsync();
    }

    void IGameLaunchHook.OnExited(LaunchContext ctx, RunningProcess process, int exitCode, bool crashed)
    {
        if (games.AnyRunning)
            return;
        _state = PresenceState.Launcher;
        _since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _join = null;
        _ = HeartbeatSafeAsync();
    }

    private async Task HeartbeatSafeAsync()
    {
        try
        {
            if (client.CanUse)
                await HeartbeatAsync(CancellationToken.None);
        }
        catch (AxoException)
        {
        }
    }

    /// <summary>"Freund ist online" und "Freund spielt auf einem deiner Server" entstehen lokal aus dem Status.</summary>
    private void DetectLocalNotifications(Dictionary<string, Friend> previous)
    {
        var scope = settings.Current.Notifications.FriendOnServerScope;
        HashSet<string>? myServers = null;
        foreach (var friend in _friends.Values.Where(f => f.Relation == Relation.Friends))
        {
            previous.TryGetValue(friend.Uuid, out var before);
            if (before != null && before.State == PresenceState.Offline && friend.State != PresenceState.Offline)
                notifications.Add("friendOnline", TextRef.Of("notifications.friendOnline", ("name", friend.Name)),
                    new() { ["uuid"] = friend.Uuid }, $"online:{friend.Uuid}");

            if (friend.Join == null || friend.State != PresenceState.Ingame)
                continue;
            myServers ??= CollectServers(scope);
            var normalized = ServerPing.Normalize(friend.Join);
            if (myServers.Contains(normalized) && _onServerNotified.Add($"{friend.Uuid}|{normalized}"))
                notifications.Add("friendOnServer", TextRef.Of("notifications.friendOnServer", ("name", friend.Name), ("server", friend.Join)),
                    new() { ["uuid"] = friend.Uuid, ["address"] = friend.Join });
            if (friend.Join.Contains(".e4mc.link", StringComparison.OrdinalIgnoreCase) && before?.Join != friend.Join)
                notifications.Add("friendOpenWorld", TextRef.Of("notifications.friendOpenWorld", ("name", friend.Name)),
                    new() { ["uuid"] = friend.Uuid, ["address"] = friend.Join });
        }
    }

    private HashSet<string> CollectServers(FriendOnServerScope scope)
    {
        var result = new HashSet<string>();
        try
        {
            IReadOnlyList<string> ids = scope == FriendOnServerScope.All
                ? instances.Ids()
                : _activeInstance != null ? [_activeInstance] : settings.State.LastInstance is { } last ? [last] : [];
            foreach (var id in ids)
                result.UnionWith(serverLists.NormalizedAddresses(id));
        }
        catch (Exception ex) when (ex is IOException or AxoException)
        {
        }
        return result;
    }

    // ---- Freunde ----

    public async Task SendRequestAsync(string name, CancellationToken ct)
    {
        await client.SendAsync(HttpMethod.Post, "/friends/requests", new { name = Text.Clean(name, 16) }, ct);
        await SyncNowAsync(ct);
    }

    public async Task AcceptAsync(string uuid, CancellationToken ct)
    {
        await client.SendAsync(HttpMethod.Post, $"/friends/{RequireUuid(uuid)}", ct: ct);
        await SyncNowAsync(ct);
    }

    /// <summary>Anfrage zurückziehen oder ablehnen.</summary>
    public async Task DeclineAsync(string uuid, CancellationToken ct)
    {
        await client.SendAsync(HttpMethod.Delete, $"/friends/requests/{RequireUuid(uuid)}", ct: ct);
        await SyncNowAsync(ct);
    }

    public async Task RemoveAsync(string uuid, CancellationToken ct)
    {
        await client.SendAsync(HttpMethod.Delete, $"/friends/{RequireUuid(uuid)}", ct: ct);
        await SyncNowAsync(ct);
    }

    public async Task BlockAsync(string uuid, bool block, CancellationToken ct)
    {
        await client.SendAsync(block ? HttpMethod.Post : HttpMethod.Delete, $"/blocks/{RequireUuid(uuid)}", ct: ct);
        try
        {
            await LoadBlockedAsync(ct);
        }
        catch (Exception ex) when (ex is AxoException or HttpRequestException or IOException)
        {
            // Blockieren hat geklappt; die Liste kommt beim nächsten Öffnen
            AppLog.Info($"Blockliste nicht geladen: {ex.Message}");
        }
        await SyncNowAsync(ct);
    }

    /// <summary>Eigene Blockliste vom Service holen; meldet sie als <c>social.blocked</c>.</summary>
    public async Task<IReadOnlyList<BlockedPlayer>> LoadBlockedAsync(CancellationToken ct)
    {
        var response = await client.SendAsync<BlocksResponse>(HttpMethod.Get, "/blocks", ct: ct);
        Blocked = response?.Blocks ?? [];
        events.Publish("social.blocked", Blocked);
        return Blocked;
    }

    public async Task MarkNotificationsReadAsync(CancellationToken ct)
    {
        notifications.MarkAllRead();
        if (client.CanUse && client.Available)
            await client.SendAsync(HttpMethod.Post, "/notifications/read", new { all = true }, ct);
    }

    public async Task DeleteAccountAsync(CancellationToken ct)
    {
        await client.SendAsync(HttpMethod.Delete, "/me", ct: ct);
        _friends = new();
        Inbox = [];
        Blocked = [];
        Me = null;
    }

    internal static string RequireUuid(string uuid)
    {
        var compact = uuid.Replace("-", "").ToLowerInvariant();
        return compact.Length == 32 && compact.All(Uri.IsHexDigit) ? compact : throw new AxoException("errors.invalidId");
    }
}
