using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.DataModel.Settings;
using AxoClient.DataModel.Social;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Settings.Contract;
using AxoClient.Social.Contract;

namespace AxoClient.Social
{
    /// <summary>
    /// Freunde und Status. Ein <c>GET /v2/sync</c> alle 30–60 Sekunden statt vieler Einzelabfragen, danach der Heartbeat.
    /// Ist der Service nicht erreichbar, heißt es "nicht verfügbar" und es wird später erneut versucht, ohne Dialog.
    /// </summary>
    public sealed class SocialService : IGameLaunchHook, ISocialService
    {
        private readonly IOnlineClient _client;
        private readonly INotificationManager _notifications;
        private readonly ISettingsService _settings;
        private readonly IGameLauncher _games;
        private readonly IInstanceManager _instances;
        private readonly IServerListManager _serverLists;
        private readonly IEventHub _events;
        private readonly ITextHelper _textHelper;
        private readonly IAppLog _appLog;
        private readonly IServerPing _serverPing;
        private readonly IUuidHelper _uuidHelper;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(45);
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

        public SocialService(
            IOnlineClient client,
            INotificationManager notifications,
            ISettingsService settings,
            IGameLauncher games,
            IInstanceManager instances,
            IServerListManager serverLists,
            IEventHub events,
            ITextHelper textHelper,
            IAppLog appLog,
            IServerPing serverPing,
            IUuidHelper uuidHelper)
        {
            _client = client;
            _notifications = notifications;
            _settings = settings;
            _games = games;
            _instances = instances;
            _serverLists = serverLists;
            _events = events;
            _textHelper = textHelper;
            _appLog = appLog;
            _serverPing = serverPing;
            _uuidHelper = uuidHelper;
        }

        public Me? Me { get; private set; }
        public IReadOnlyList<Friend> Friends => _friends.Values.OrderBy(f => f.Relation).ThenByDescending(f => f.State)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        public IReadOnlyList<InboxItem> Inbox { get; private set; } = [];

        /// <summary>Zuletzt geladene Blockliste (leer, bis <see cref="LoadBlocked"/> lief).</summary>
        public IReadOnlyList<BlockedPlayer> Blocked { get; private set; } = [];
        public bool Available => _client.Available && _client.CanUse;

        public void Start()
        {
            Stop();
            _loop = new CancellationTokenSource();
            _ = loop(_loop.Token);
        }

        public void Stop()
        {
            _loop?.Cancel();
            _loop = null;
        }

        /// <summary>Sofort synchronisieren (z. B. nach dem Senden einer Anfrage).</summary>
        public async Task SyncNow(CancellationToken ct = default)
        {
            if (!_client.CanUse)
            {
                return;
            }

            var sync = await _client.Send<SyncResponse>(HttpMethod.Get, "/sync", ct: ct);
            if (sync == null)
            {
                return;
            }

            Me = sync.Me;
            var previous = _friends;
            _friends = sync.Friends.ToDictionary(f => f.Uuid);
            Inbox = sync.Inbox;
            _notifications.ApplyRemote(sync.Notifications);
            foreach (var n in sync.Notifications.Where(n => _seenRemote.Add(n.Id)))
            {
                RemoteNotification?.Invoke(n);
            }

            detectLocalNotifications(previous);
            _events.Publish("social.changed", new { friends = Friends, inbox = Inbox, me = Me, available = true });
        }

        private async Task loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await SyncNow(ct);
                    await heartbeat(ct);
                }
                catch (AxoException ex) when (ex.Code is "service.unavailable" or "service.notSignedIn")
                {
                    _events.Publish("social.changed", new { friends = Array.Empty<Friend>(), inbox = Array.Empty<InboxItem>(), me = (Me?)null, available = false });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _appLog.Warn("Abgleich mit dem Online-Service fehlgeschlagen", ex);
                }
                try
                {
                    await Task.Delay(_interval, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task heartbeat(CancellationToken ct)
        {
            var social = _settings.Current.Social;
            if (!social.ShowOnline)
            {
                await _client.Send(HttpMethod.Put, "/status", new { state = "offline", since = _since, join = (string?)null }, ct);
                return;
            }
            await _client.Send(HttpMethod.Put, "/status", new
            {
                state = _state.ToString().ToLowerInvariant(),
                since = _since,
                join = social.AllowJoin && !_settings.Current.Integrations.StreamerMode ? _join : null
            }, ct);
        }

        /// <summary>Wo man gerade spielt (Server-Adresse oder e4mc-Adresse der offenen Welt), damit Freunde nachjoinen können.</summary>
        public void SetJoinAddress(string? address)
        {
            _join = address != null && _serverPing.IsValidAddress(address) ? address : null;
            _ = heartbeatSafe();
        }

        void IGameLaunchHook.OnStarted(LaunchContext ctx, RunningProcess process)
        {
            _state = PresenceState.Ingame;
            _since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _activeInstance = ctx.Instance.Id;
            _join = ctx.QuickPlay?.Server;
            _onServerNotified.Clear();
            _ = heartbeatSafe();
        }

        void IGameLaunchHook.OnExited(LaunchContext ctx, RunningProcess process, int exitCode, bool crashed)
        {
            if (_games.AnyRunning)
            {
                return;
            }

            _state = PresenceState.Launcher;
            _since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _join = null;
            _ = heartbeatSafe();
        }

        private async Task heartbeatSafe()
        {
            try
            {
                if (_client.CanUse)
                {
                    await heartbeat(CancellationToken.None);
                }
            }
            catch (AxoException)
            {
            }
        }

        /// <summary>"Freund ist online" und "Freund spielt auf einem deiner Server" entstehen lokal aus dem Status.</summary>
        private void detectLocalNotifications(Dictionary<string, Friend> previous)
        {
            var scope = _settings.Current.Notifications.FriendOnServerScope;
            HashSet<string>? myServers = null;
            foreach (var friend in _friends.Values.Where(f => f.Relation == Relation.Friends))
            {
                previous.TryGetValue(friend.Uuid, out var before);
                if (before != null && before.State == PresenceState.Offline && friend.State != PresenceState.Offline)
                {
                    _notifications.Add("friendOnline", new TextRef("notifications.friendOnline", ("name", friend.Name)),
                        new() { ["uuid"] = friend.Uuid }, $"online:{friend.Uuid}");
                }

                if (friend.Join == null || friend.State != PresenceState.Ingame)
                {
                    continue;
                }

                myServers ??= collectServers(scope);
                var normalized = _serverPing.Normalize(friend.Join);
                if (myServers.Contains(normalized) && _onServerNotified.Add($"{friend.Uuid}|{normalized}"))
                {
                    _notifications.Add("friendOnServer", new TextRef("notifications.friendOnServer", ("name", friend.Name), ("server", friend.Join)),
                        new() { ["uuid"] = friend.Uuid, ["address"] = friend.Join });
                }

                if (friend.Join.Contains(".e4mc.link", StringComparison.OrdinalIgnoreCase) && before?.Join != friend.Join)
                {
                    _notifications.Add("friendOpenWorld", new TextRef("notifications.friendOpenWorld", ("name", friend.Name)),
                        new() { ["uuid"] = friend.Uuid, ["address"] = friend.Join });
                }
            }
        }

        private HashSet<string> collectServers(FriendOnServerScope scope)
        {
            var result = new HashSet<string>();
            try
            {
                IReadOnlyList<string> ids = scope == FriendOnServerScope.All
                    ? _instances.Ids()
                    : _activeInstance != null ? [_activeInstance] : _settings.State.LastInstance is { } last ? [last] : [];
                foreach (var id in ids)
                {
                    result.UnionWith(_serverLists.NormalizedAddresses(id));
                }
            }
            catch (Exception ex) when (ex is IOException or AxoException)
            {
            }
            return result;
        }

        // ---- Freunde ----

        public async Task SendRequest(string name, CancellationToken ct)
        {
            await _client.Send(HttpMethod.Post, "/friends/requests", new { name = _textHelper.Clean(name, 16) }, ct);
            await SyncNow(ct);
        }

        public async Task Accept(string uuid, CancellationToken ct)
        {
            await _client.Send(HttpMethod.Post, $"/friends/{_uuidHelper.RequireUuid(uuid)}", ct: ct);
            await SyncNow(ct);
        }

        /// <summary>Anfrage zurückziehen oder ablehnen.</summary>
        public async Task Decline(string uuid, CancellationToken ct)
        {
            await _client.Send(HttpMethod.Delete, $"/friends/requests/{_uuidHelper.RequireUuid(uuid)}", ct: ct);
            await SyncNow(ct);
        }

        public async Task Remove(string uuid, CancellationToken ct)
        {
            await _client.Send(HttpMethod.Delete, $"/friends/{_uuidHelper.RequireUuid(uuid)}", ct: ct);
            await SyncNow(ct);
        }

        public async Task Block(string uuid, bool block, CancellationToken ct)
        {
            await _client.Send(block ? HttpMethod.Post : HttpMethod.Delete, $"/blocks/{_uuidHelper.RequireUuid(uuid)}", ct: ct);
            try
            {
                await LoadBlocked(ct);
            }
            catch (Exception ex) when (ex is AxoException or HttpRequestException or IOException)
            {
                // Blockieren hat geklappt; die Liste kommt beim nächsten Öffnen
                _appLog.Info($"Blockliste nicht geladen: {ex.Message}");
            }
            await SyncNow(ct);
        }

        /// <summary>Eigene Blockliste vom Service holen; meldet sie als <c>social.blocked</c>.</summary>
        public async Task<IReadOnlyList<BlockedPlayer>> LoadBlocked(CancellationToken ct)
        {
            var response = await _client.Send<BlocksResponse>(HttpMethod.Get, "/blocks", ct: ct);
            Blocked = response?.Blocks ?? [];
            _events.Publish("social.blocked", Blocked);
            return Blocked;
        }

        public async Task MarkNotificationsRead(CancellationToken ct)
        {
            _notifications.MarkAllRead();
            if (_client.CanUse && _client.Available)
            {
                await _client.Send(HttpMethod.Post, "/notifications/read", new { all = true }, ct);
            }
        }

        public async Task DeleteAccount(CancellationToken ct)
        {
            await _client.Send(HttpMethod.Delete, "/me", ct: ct);
            _friends = new();
            Inbox = [];
            Blocked = [];
            Me = null;
        }
    }
}
