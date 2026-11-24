using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Accounts;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Instances;
using AxoClient.DataModel.Launch;
using AxoClient.DataModel.Skins;
using AxoClient.DataModel.Social;
using AxoClient.Instances.Contract;
using AxoClient.Integrations.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Skins.Contract;
using AxoClient.Social.Contract;

namespace AxoClient.Integrations
{
    /// <summary>
    /// Das Axo-Menü im Spiel spricht über die <see cref="AxoModBridge"/> mit dem Launcher: Freunde und Anfragen, Einladen,
    /// Nachjoinen (gleiche Instanz, andere Instanz, fehlende Mods), Capes, Emotes und kleine Einblendungen. Die Mod bekommt
    /// nur fertige Daten, nie das Token des Online-Service.
    /// </summary>
    public sealed partial class IngameLink : IIngameLink
    {
        private readonly IAxoModBridge _bridge;
        private readonly ISocialService _social;
        private readonly IInboxManager _inbox;
        private readonly IOnlineClient _online;
        private readonly ICapeService _capeService;
        private readonly ICapeManager _capes;
        private readonly IMojangApi _mojang;
        private readonly IAccountManager _accounts;
        private readonly ISkinManager _skins;
        private readonly IInstanceManager _instances;
        private readonly IServerListManager _serverLists;
        private readonly IContentManager _content;
        private readonly IModrinthProvider _modrinth;
        private readonly IGameLauncher _games;
        private readonly IGameActivity _activity;
        private readonly ILiveChannel _live;
        private readonly IEmoteLibrary _emotes;
        private readonly IApiClient _api;
        private readonly IEventHub _events;
        private readonly IJobManager _jobs;
        private readonly ITextHelper _textHelper;
        private readonly IAppLog _appLog;
        private readonly IAppInfo _appInfo;
        private readonly IServerPing _serverPing;
        /// <summary>Größte Cape-Grafik, die an die Mod geht (Base64-Zeichen).</summary>
        private const int MaxImageChars = 180_000;

        private readonly ConcurrentDictionary<string, HashSet<string>> _sentSkins = new();
        private readonly ConcurrentDictionary<string, string> _invited = new();
        private readonly ConcurrentDictionary<string, JoinPlan> _plans = new();
        private readonly ConcurrentDictionary<string, string> _capeImages = new();
        private readonly SemaphoreSlim _skinLock = new(1, 1);
        private string? _hostedWorld;

        public IngameLink(
            IAxoModBridge bridge,
            ISocialService social,
            IInboxManager inbox,
            IOnlineClient online,
            ICapeService capeService,
            ICapeManager capes,
            IMojangApi mojang,
            IAccountManager accounts,
            ISkinManager skins,
            IInstanceManager instances,
            IServerListManager serverLists,
            IContentManager content,
            IModrinthProvider modrinth,
            IGameLauncher games,
            IGameActivity activity,
            ILiveChannel live,
            IEmoteLibrary emotes,
            IApiClient api,
            IEventHub events,
            IJobManager jobs,
            ITextHelper textHelper,
            IAppLog appLog,
            IAppInfo appInfo,
            IServerPing serverPing)
        {
            _bridge = bridge;
            _social = social;
            _inbox = inbox;
            _online = online;
            _capeService = capeService;
            _capes = capes;
            _mojang = mojang;
            _accounts = accounts;
            _skins = skins;
            _instances = instances;
            _serverLists = serverLists;
            _content = content;
            _modrinth = modrinth;
            _games = games;
            _activity = activity;
            _live = live;
            _emotes = emotes;
            _api = api;
            _events = events;
            _jobs = jobs;
            _textHelper = textHelper;
            _appLog = appLog;
            _appInfo = appInfo;
            _serverPing = serverPing;
        }

        public void Start()
        {
            _bridge.MessageReceived += (instance, type, data) => _ = handle(instance, type, data);
            _events.Published += onEvent;
            _live.EmoteReceived += remoteEmote => _ = _bridge.Send(null, "emote", new { uuid = remoteEmote.Uuid, emote = remoteEmote.Emote, anim = remoteEmote.Anim, animation = remoteEmote.Animation });
            _activity.JoinAddressChanged += address =>
            {
                // Neuer Ort: alte Einladungen gelten nicht mehr
                _invited.Clear();
                _ = pushSocial(null);
                _ = announceWorld(address);
            };
        }

        // ---------- Nachrichten der Mod ----------

        private async Task handle(string instance, string type, JsonElement data)
        {
            try
            {
                switch (type)
                {
                    case "hello":
                    case "retry":
                        _sentSkins.TryRemove(instance, out _);
                        await pushSocial(instance);
                        await pushEmotes(instance);
                        await pushCapes(instance);
                        _ = refreshBlocked();
                        break;
                    case "friendAccept":
                        await _social.Accept(uuid(data), CancellationToken.None);
                        break;
                    case "friendDecline":
                        await _social.Decline(uuid(data), CancellationToken.None);
                        break;
                    case "unblock":
                        await _social.Block(uuid(data), false, CancellationToken.None);
                        break;
                    case "block":
                        // Nur als "Rückgängig" nach dem Entblockieren im Menü
                        await _social.Block(uuid(data), true, CancellationToken.None);
                        break;
                    case "invite":
                        await invite(instance, uuid(data));
                        break;
                    case "inviteUndo":
                        await inviteUndo(instance, uuid(data));
                        break;
                    case "inviteDecline":
                        await _inbox.Discard(readString(data, "id") ?? "", CancellationToken.None);
                        break;
                    case "joinCheck":
                        await _bridge.Send(instance, "joinPlan", await plan(instance, data));
                        break;
                    case "join":
                        await join(instance, data);
                        break;
                    case "capeWear":
                        await wearCape(readString(data, "key") ?? "none");
                        await pushCapes(null);
                        break;
                    case "emoteSlots":
                        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Array)
                        {
                            await _emotes.SetSlots(slots.EnumerateArray().Select(slot => slot.ValueKind == JsonValueKind.String ? slot.GetString() : null).ToList());
                        }

                        break;
                    case "emote":
                        await sendEmote(readString(data, "emote"));
                        break;
                    case "openLauncher":
                        _events.Publish("app.focus");
                        break;
                }
            }
            catch (Exception exception) when (exception is AxoException or HttpRequestException or IOException or JsonException or InvalidOperationException
                                           or KeyNotFoundException or TaskCanceledException)
            {
                _appLog.Info($"Ingame-Menü: {type} fehlgeschlagen: {exception.Message}");
                var code = exception is AxoException axoException ? axoException.Text.Code : "errors.unknown";
                if (type == "join" || type == "joinCheck")
                {
                    await _bridge.Send(instance, "joinState", new { uuid = readString(data, "uuid"), phase = "error", code });
                }
                else
                {
                    await _bridge.Send(instance, "error", new { action = type, code });
                }
            }
        }

        private void onEvent(string type, object? data)
        {
            switch (type)
            {
                case "social.changed":
                case "social.blocked":
                    _ = pushSocial(null);
                    break;
                case "emotes.changed":
                    _ = pushEmotes(null);
                    break;
                case "capes.changed":
                case "accounts.changed":
                    _ = pushCapes(null);
                    break;
                case "notification.new" when data is NotificationItem item:
                    _ = toast(item);
                    break;
            }
        }

        // ---------- Freunde ----------

        /// <summary>Blockliste für das Menü nachladen; ohne Anmeldung oder Service bleibt sie still leer.</summary>
        private async Task refreshBlocked()
        {
            if (!_social.Available)
            {
                return;
            }

            try
            {
                await _social.LoadBlocked(CancellationToken.None);
            }
            catch (Exception exception) when (exception is AxoException or HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
                _appLog.Info($"Ingame-Menü: Blockliste nicht geladen: {exception.Message}");
            }
        }

        private async Task pushSocial(string? onlyInstance)
        {
            foreach (var instance in targets(onlyInstance))
            {
                var friends = _social.Friends;
                var blocked = _social.Blocked;
                var invites = _social.Inbox.Where(inboxItem => inboxItem.Kind == "invite" && inboxItem.Expires > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    .Select(inboxItem => new { id = inboxItem.Id, uuid = inboxItem.Sender.Uuid, name = inboxItem.Sender.Name, address = inboxItem.Title, created = inboxItem.Created })
                    .ToList();
                await _bridge.Send(instance, "social", new
                {
                    available = _social.Available,
                    me = _social.Me == null ? null : new { uuid = _social.Me.Uuid, name = _social.Me.Name },
                    here = await here(instance),
                    invited = _invited.Keys.ToList(),
                    invites,
                    blocked = blocked.Select(blockedPlayer => new { uuid = blockedPlayer.Uuid, name = blockedPlayer.Name }).ToList(),
                    friends = friends.Select(friend => new
                    {
                        uuid = friend.Uuid,
                        name = friend.Name,
                        relation = friend.Relation switch { Relation.Pending => "pending", Relation.Incoming => "incoming", _ => "friends" },
                        state = friend.State switch { PresenceState.Ingame => "ingame", PresenceState.Launcher => "launcher", _ => "offline" },
                        since = friend.Since,
                        join = friend.Join,
                        joinKind = friend.Join == null ? null : IsRelay(friend.Join) ? "world" : "server"
                    }).ToList()
                });
                // Auch der eigene Skin: das Menü zeigt damit die Cape-Vorschau
                var own = _accounts.Active?.Uuid.Replace("-", "").ToLowerInvariant();
                _ = sendSkins(instance, friends.Select(friend => friend.Uuid).Concat(invites.Select(pendingInvite => pendingInvite.uuid)).Concat(blocked.Select(blockedPlayer => blockedPlayer.Uuid))
                    .Concat(own is { Length: 32 } ? [own] : []));
            }
        }

        /// <summary>Wo man gerade spielt: Adresse, Art und Name aus der Serverliste der Instanz (samt Symbol).</summary>
        private async Task<object?> here(string instanceId)
        {
            var address = _activity.JoinAddress;
            if (address == null)
            {
                return null;
            }

            if (IsRelay(address))
            {
                return new { address, kind = "world", name = (string?)null, icon = (string?)null };
            }

            var normalized = _serverPing.Normalize(address);
            var entry = _serverLists.List(instanceId).FirstOrDefault(server => _serverPing.IsValidAddress(server.Address) && _serverPing.Normalize(server.Address) == normalized);
            string? icon = null;
            if (entry != null && _serverLists.Icon(instanceId, entry.Index) is { Length: > 0 and < 64 * 1024 } bytes)
            {
                icon = Convert.ToBase64String(bytes);
            }

            await Task.CompletedTask;
            return new { address, kind = "server", name = entry?.Name, icon };
        }

        private async Task sendSkins(string instance, IEnumerable<string> uuids)
        {
            var sent = _sentSkins.GetOrAdd(instance, _ => []);
            var todo = uuids.Distinct().ToList();
            await _skinLock.WaitAsync();
            try
            {
                foreach (var uuid in todo)
                {
                    lock (sent)
                    {
                        if (!sent.Add(uuid))
                        {
                            continue;
                        }
                    }

                    try
                    {
                        if (await _skins.PlayerSkin(uuid, CancellationToken.None) is { Length: > 0 and < 96 * 1024 } png)
                        {
                            await _bridge.Send(instance, "skin", new { uuid, png = Convert.ToBase64String(png) });
                        }
                    }
                    catch (Exception exception) when (exception is HttpRequestException or AxoException or IOException or TaskCanceledException)
                    {
                        lock (sent)
                        {
                            sent.Remove(uuid);
                        }
                    }
                }
            }
            finally
            {
                _skinLock.Release();
            }
        }

        private async Task invite(string instance, string uuid)
        {
            var address = _activity.JoinAddress ?? throw new AxoException("ingame.notOnServer");
            await _inbox.SendInvite(address, _activity.InstanceName, _activity.Minecraft, [uuid], CancellationToken.None);
            _invited[uuid] = address;
            await _bridge.Send(instance, "inviteState", new { uuid, invited = true });
        }

        private async Task inviteUndo(string instance, string uuid)
        {
            await _online.Send(HttpMethod.Delete, $"/invites/{uuid}", cancellationToken: CancellationToken.None);
            _invited.TryRemove(uuid, out _);
            await _bridge.Send(instance, "inviteState", new { uuid, invited = false });
        }

        private async Task toast(NotificationItem item)
        {
            string parameter(string key) => item.Text.Params?.GetValueOrDefault(key)?.ToString() ?? "";
            var kind = item.Type switch
            {
                "friendRequest" => "request",
                "friendAccepted" => "accepted",
                "friendOnline" => "online",
                "friendOnServer" => "onServer",
                "friendOpenWorld" => "openWorld",
                "invite" => "invite",
                "inbox" => "share",
                _ => null
            };
            if (kind == null)
            {
                return;
            }

            var name = parameter("name");
            var uuid = _social.Friends.FirstOrDefault(friend => friend.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Uuid;
            await _bridge.Send(null, "toast", new
            {
                kind,
                name,
                uuid,
                server = parameter("server") is { Length: > 0 } serverName ? serverName : null,
                title = parameter("title") is { Length: > 0 } toastTitle ? toastTitle : null
            });
            if (uuid != null)
            {
                foreach (var instance in _bridge.ConnectedInstances)
                {
                    _ = sendSkins(instance, [uuid]);
                }
            }
        }

        // ---------- Nachjoinen ----------

        private sealed record InstanceInfo(string Id, string Name, string Minecraft, string Loader);

        private sealed record JoinPlan(
            string Key,
            string Kind,
            string Address,
            string? Uuid,
            string? Name,
            string? InviteId,
            string Title,
            string Meta,
            string? Right,
            string? RightColor,
            InstanceInfo Now,
            InstanceInfo? Next,
            IReadOnlyList<MissingMod> Mods,
            string? Version,
            string? Reason);

        private sealed record MissingMod(string Name, string Source, string ProjectId, string? VersionId, string Meta);

        private async Task<object> plan(string instanceId, JsonElement data)
        {
            var plan = await buildPlan(instanceId, data);
            _plans[instanceId + "|" + plan.Key] = plan;
            return new
            {
                key = plan.Key,
                uuid = plan.Uuid,
                name = plan.Name,
                inviteId = plan.InviteId,
                kind = plan.Kind,
                address = plan.Address,
                title = plan.Title,
                meta = plan.Meta,
                right = plan.Right,
                rightColor = plan.RightColor,
                now = new { name = plan.Now.Name, minecraft = plan.Now.Minecraft, loader = plan.Now.Loader },
                next = plan.Next == null ? null : new { name = plan.Next.Name, minecraft = plan.Next.Minecraft, loader = plan.Next.Loader },
                mods = plan.Mods.Select(mod => new { name = mod.Name, meta = mod.Meta }).ToList(),
                version = plan.Version,
                reason = plan.Reason,
                here = _activity.JoinAddress
            };
        }

        private async Task<JoinPlan> buildPlan(string instanceId, JsonElement data)
        {
            var uuid = readString(data, "uuid");
            var inviteId = readString(data, "inviteId");
            string? address = null;
            string? name = null;
            if (inviteId != null)
            {
                var invite = _social.Inbox.FirstOrDefault(inboxItem => inboxItem.Id == inviteId) ?? throw new AxoException("ingame.inviteGone");
                name = invite.Sender.Name;
                uuid ??= invite.Sender.Uuid;
                var item = await _inbox.Fetch(inviteId, CancellationToken.None);
                if (item.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.String)
                {
                    using var document = JsonDocument.Parse(payload.GetString()!);
                    address = readString(document.RootElement, "address");
                }
            }
            else if (uuid != null)
            {
                var friend = _social.Friends.FirstOrDefault(knownFriend => knownFriend.Uuid == uuid) ?? throw new AxoException("ingame.friendGone");
                name = friend.Name;
                address = friend.Join;
            }
            if (address == null || !_serverPing.IsValidAddress(address))
            {
                throw new AxoException("ingame.noJoin");
            }

            var current = await _instances.Get(instanceId);
            var now = getInfo(current);
            var world = IsRelay(address);
            var key = inviteId ?? uuid ?? address;

            var pingTask = getPing(address);
            var worldTask = world ? _live.PeekWorld(address, TimeSpan.FromSeconds(2.5), CancellationToken.None) : Task.FromResult<JsonElement?>(null);
            await Task.WhenAll(pingTask, worldTask);
            var ping = await pingTask;
            var hostWorldInfo = await worldTask;

            var hostVersion = hostWorldInfo is { } infoForVersion ? readString(infoForVersion, "minecraft") : null;
            var hostLoader = hostWorldInfo is { } infoForLoader ? readString(infoForLoader, "loader") : null;
            bool Fits(Instance inst)
            {
                if (hostVersion != null)
                {
                    return inst.Minecraft.Version == hostVersion
                           && (hostLoader == null || inst.Loader.Type.ToString().Equals(hostLoader, StringComparison.OrdinalIgnoreCase));
                }

                return VersionFits(ping?.Version, inst.Minecraft.Version) != false;
            }

            var players = ping is { Online: true } ? $"{ping.Players:N0} / {ping.MaxPlayers:N0} Spieler".Replace(',', '.') : null;
            string title, meta;
            string? right, rightColor;
            if (world)
            {
                title = $"Offene Welt von {name}";
                meta = string.Join(" · ", new[] { "Offene Welt", hostVersion, hostLoader is { Length: > 0 } loaderName ? char.ToUpperInvariant(loaderName[0]) + loaderName[1..] : null,
                    ping is { Online: true } ? $"{ping.Players} Spieler" : null }.Where(part => part != null));
                right = "e4mc";
                rightColor = "#bdbdbd";
            }
            else
            {
                var normalized = _serverPing.Normalize(address);
                title = _serverLists.List(instanceId).FirstOrDefault(server => _serverPing.IsValidAddress(server.Address) && _serverPing.Normalize(server.Address) == normalized)?.Name
                        ?? firstLine(ping?.Motd) ?? address;
                meta = players == null ? address : $"{address} · {players}";
                right = ping?.LatencyMs is { } latency ? $"{latency} ms" : ping is { Online: false } ? "offline" : null;
                rightColor = ping?.LatencyMs is { } latencyForColor ? latencyForColor < 80 ? "#3fb950" : latencyForColor < 150 ? "#bdbdbd" : "#e3b341" : "#e3b341";
            }

            if (!Fits(current))
            {
                // Andere Instanz nötig: erst eine, die den Server schon kennt, sonst die zuletzt gespielte passende
                var all = await _instances.LoadAll();
                var candidates = new List<(Instance Instance, bool KnowsServer, DateTimeOffset LastPlayed)>();
                foreach (var inst in all.Where(instance => instance.Id != instanceId && Fits(instance)))
                {
                    var knows = !world && _serverLists.NormalizedAddresses(inst.Id).Contains(_serverPing.Normalize(address));
                    var summary = await _instances.Summary(inst);
                    candidates.Add((inst, knows, summary.LastPlayed ?? inst.CreatedAt));
                }
                var best = candidates.OrderByDescending(candidate => candidate.KnowsServer).ThenByDescending(candidate => candidate.LastPlayed).FirstOrDefault();
                var version = hostVersion ?? versionText(ping?.Version);
                if (best.Instance == null)
                {
                    return new JoinPlan(key, "none", address, uuid, name, inviteId, title, meta, right, rightColor, now, null, [], version, "noInstance");
                }

                return new JoinPlan(key, "instance", address, uuid, name, inviteId, title, meta, right, rightColor, now, getInfo(best.Instance), [], version, null);
            }

            if (hostWorldInfo is { } worldInfo && worldInfo.TryGetProperty("mods", out var modsElement) && modsElement.ValueKind == JsonValueKind.Array)
            {
                var missing = await missingMods(instanceId, current, modsElement);
                if (missing.Count > 0)
                {
                    return new JoinPlan(key, "mods", address, uuid, name, inviteId, title, meta, right, rightColor, now, null, missing, hostVersion, null);
                }
            }
            return new JoinPlan(key, world ? "world" : "server", address, uuid, name, inviteId, title, meta, right, rightColor, now, null, [], null, null);
        }

        /// <summary>Mods des Gastgebers (Modrinth), die in der eigenen Instanz fehlen – ohne reine Client-Mods.</summary>
        private async Task<IReadOnlyList<MissingMod>> missingMods(string instanceId, Instance current, JsonElement mods)
        {
            var have = (await _content.Sync(instanceId)).Mods.Where(mod => mod.ProjectId != null).Select(mod => mod.ProjectId!).ToHashSet();
            var wanted = mods.EnumerateArray()
                .Where(mod => mod.ValueKind == JsonValueKind.Object && readString(mod, "source") == "modrinth" && readString(mod, "project") is { } project && projectIdPattern().IsMatch(project))
                .Select(mod => new MissingMod(_textHelper.Clean(readString(mod, "name"), 60) is { Length: > 0 } modName ? modName : readString(mod, "project")!, "modrinth", readString(mod, "project")!,
                    readString(mod, "version") is { } version && projectIdPattern().IsMatch(version) ? version : null,
                    $"{_textHelper.Clean(readString(mod, "modVersion"), 24)} · {current.Loader.Type}".TrimStart(' ', '·')))
                .Where(missingMod => !have.Contains(missingMod.ProjectId))
                .DistinctBy(missingMod => missingMod.ProjectId)
                .Take(60)
                .ToList();
            if (wanted.Count == 0)
            {
                return [];
            }

            try
            {
                var sides = await _modrinth.ServerSides(wanted.Select(missingMod => missingMod.ProjectId), CancellationToken.None);
                return wanted.Where(missingMod => sides.GetValueOrDefault(missingMod.ProjectId) != "unsupported").ToList();
            }
            catch (Exception exception) when (exception is HttpRequestException or AxoException or JsonException or TaskCanceledException)
            {
                return wanted;
            }
        }

        private async Task join(string instanceId, JsonElement data)
        {
            var key = readString(data, "inviteId") ?? readString(data, "uuid") ?? "";
            if (!_plans.TryGetValue(instanceId + "|" + key, out var plan))
            {
                plan = await buildPlan(instanceId, data);
            }

            var state = new { uuid = plan.Uuid, key = plan.Key };
            switch (plan.Kind)
            {
                case "server":
                case "world":
                    await consumeInvite(plan);
                    await _bridge.Send(instanceId, "connect", new { address = plan.Address, name = plan.Title });
                    await _bridge.Send(instanceId, "joinState", new { state.uuid, state.key, phase = "done" });
                    break;
                case "instance" when plan.Next != null:
                    await consumeInvite(plan);
                    await _bridge.Send(instanceId, "joinState", new { state.uuid, state.key, phase = "busy" });
                    _ = restartInto(instanceId, plan.Next.Id, plan.Address);
                    break;
                case "mods":
                    _events.Publish("ingame.missingMods", new
                    {
                        instanceId,
                        friend = plan.Name,
                        address = plan.Address,
                        title = plan.Title,
                        mods = plan.Mods.Select(mod => new { name = mod.Name, source = mod.Source, projectId = mod.ProjectId, versionId = mod.VersionId, meta = mod.Meta })
                    });
                    _events.Publish("app.focus");
                    await _bridge.Send(instanceId, "joinState", new { state.uuid, state.key, phase = "launcher" });
                    break;
                default:
                    _events.Publish("app.focus");
                    await _bridge.Send(instanceId, "joinState", new { state.uuid, state.key, phase = "launcher" });
                    break;
            }
        }

        /// <summary>Eine angenommene Einladung aus dem Postfach nehmen.</summary>
        private async Task consumeInvite(JoinPlan plan)
        {
            if (plan.InviteId != null)
            {
                try
                {
                    await _inbox.Discard(plan.InviteId, CancellationToken.None);
                }
                catch (Exception exception) when (exception is AxoException or HttpRequestException)
                {
                }
            }
        }

        /// <summary>Spiel sauber beenden lassen (die Mod schließt Minecraft), dann die andere Instanz direkt auf den Server starten.</summary>
        private async Task restartInto(string runningId, string nextId, string address)
        {
            try
            {
                await _bridge.Send(runningId, "quit", new { });
                var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(25);
                while (_games.IsRunning(runningId) && DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(500);
                }

                if (_games.IsRunning(runningId))
                {
                    await _games.Stop(runningId);
                }

                await Task.Delay(500);
                await _games.Launch(nextId, new QuickPlay(Server: address));
            }
            catch (Exception exception) when (exception is AxoException or IOException or InvalidOperationException)
            {
                _appLog.Info($"Nachjoinen mit anderer Instanz fehlgeschlagen: {exception.Message}");
                _events.Publish("app.focus");
            }
        }

        /// <summary>
        /// Fehlende Mods installieren (aus dem Dialog im Launcher) und danach das Spiel neu starten, direkt in die Welt des Freundes.
        /// </summary>
        public Task InstallMissing(string instanceId, string address, IReadOnlyList<(string Source, string ProjectId, string? VersionId)> mods) =>
            _jobs.Run("content.install", new TextRef("ingame.installingMissing"), async job =>
            {
                foreach (var (source, projectId, versionId) in mods)
                {
                    if (!projectIdPattern().IsMatch(projectId) || versionId != null && !projectIdPattern().IsMatch(versionId))
                    {
                        continue;
                    }

                    try
                    {
                        await _content.Install(instanceId, ContentType.Mod, new InstallRequest(source, projectId, versionId), job);
                    }
                    catch (AxoException) when (versionId != null)
                    {
                        // Version des Gastgebers passt nicht: die beste für die Instanz
                        await _content.Install(instanceId, ContentType.Mod, new InstallRequest(source, projectId), job);
                    }
                }
                if (_games.IsRunning(instanceId))
                {
                    _ = restartInto(instanceId, instanceId, address);
                }
                else
                {
                    await _games.Launch(instanceId, new QuickPlay(Server: address));
                }
            }, instanceId);

        /// <summary>Eigene offene Welt im Live-Raum bekannt machen (Version, Loader, Mods), damit Freunde vorab sehen, was fehlt.</summary>
        private async Task announceWorld(string? address)
        {
            try
            {
                if (address == null || !IsRelay(address))
                {
                    if (_hostedWorld != null)
                    {
                        _hostedWorld = null;
                        await _live.SendWorld(null);
                    }
                    return;
                }
                _hostedWorld = address;
                var running = _games.Running.FirstOrDefault();
                if (running == null)
                {
                    return;
                }

                var inst = await _instances.Get(running.InstanceId);
                var document = await _content.Sync(inst.Id);
                var mods = document.Mods.Where(mod => mod.Enabled && mod.Source == ContentSource.Modrinth && mod.ProjectId != null)
                    .Select(mod => new { name = mod.Name, source = "modrinth", project = mod.ProjectId, version = mod.VersionId, modVersion = mod.Version })
                    .Take(300).ToList();
                await _live.SendWorld(new { minecraft = inst.Minecraft.Version, loader = inst.Loader.Type.ToString().ToLowerInvariant(), mods });
            }
            catch (Exception exception) when (exception is AxoException or IOException or JsonException)
            {
                _appLog.Info($"Welt-Infos nicht gesendet: {exception.Message}");
            }
        }

        // ---------- Capes ----------

        private async Task pushCapes(string? onlyInstance)
        {
            var account = _accounts.Active;
            if (account == null)
            {
                return;
            }

            var list = new List<object> { new { key = "none", name = "Kein Cape", kind = "none" } };
            var images = new List<(string Key, Func<Task<byte[]?>> Load)>();
            string? mojangActive = null;
            try
            {
                var profile = await _mojang.GetOwnProfile(CancellationToken.None);
                foreach (var cape in profile.Capes)
                {
                    var key = "mojang:" + cape.Id;
                    list.Add(new { key, name = cape.Alias, kind = "mojang" });
                    if (cape.Active)
                    {
                        mojangActive = key;
                    }

                    if (cape.Url is { } url)
                    {
                        images.Add((key, async () => await _mojang.DownloadTexture(url.Replace("http://", "https://"), CancellationToken.None)));
                    }
                }
            }
            catch (Exception exception) when (exception is AxoException or HttpRequestException or JsonException or TaskCanceledException)
            {
            }
            try
            {
                foreach (var cape in await _capeService.Official(CancellationToken.None))
                {
                    var key = "official:" + cape.Id;
                    list.Add(new { key, name = cape.Name, kind = "official" });
                    images.Add((key, () => downloadServiceImage(cape.Url)));
                }
            }
            catch (Exception exception) when (exception is AxoException or HttpRequestException or JsonException or TaskCanceledException)
            {
            }
            foreach (var cape in (await _capes.List()).Where(capeView => capeView.Status == CapeStatus.Approved))
            {
                var key = "custom:" + cape.Id;
                list.Add(new { key, name = cape.Name, kind = "custom" });
                images.Add((key, async () => await _capes.Read(cape.Id, CancellationToken.None)));
            }
            var worn = account.Capes.Axo is { } axoCape
                ? (axoCape.Source == AxoCapeSource.Official ? "official:" : "custom:") + axoCape.Id
                : mojangActive ?? "none";

            foreach (var instance in targets(onlyInstance))
            {
                await _bridge.Send(instance, "capes", new { worn, list });
                foreach (var (key, load) in images)
                {
                    if (!_capeImages.TryGetValue(key, out var png))
                    {
                        try
                        {
                            if (await load() is { Length: > 0 } bytes)
                            {
                                _capeImages[key] = png = Convert.ToBase64String(bytes);
                            }
                        }
                        catch (Exception exception) when (exception is AxoException or HttpRequestException or IOException or TaskCanceledException)
                        {
                        }
                    }
                    if (png is { Length: < MaxImageChars })
                    {
                        await _bridge.Send(instance, "capeImage", new { key, png });
                    }
                }
            }
        }

        private async Task<byte[]?> downloadServiceImage(string url)
        {
            // Nur Bilder vom eigenen Dienst
            var absolute = url.StartsWith('/') ? new Uri(new Uri(_appInfo.ServiceUrl), url) : new Uri(url);
            if (absolute.Host != new Uri(_appInfo.ServiceUrl).Host)
            {
                return null;
            }

            using var response = await _api.Send(() => new HttpRequestMessage(HttpMethod.Get, absolute), CancellationToken.None);
            await _api.EnsureSuccess(response, CancellationToken.None);
            return await response.Content.ReadAsByteArrayAsync();
        }

        private async Task wearCape(string key)
        {
            var parts = key.Split(':', 2);
            switch (parts[0])
            {
                case "none":
                    await _capeService.SetActive(null, CancellationToken.None);
                    await _mojang.SetCape(null, CancellationToken.None);
                    break;
                case "mojang" when parts.Length == 2:
                    await _capeService.SetActive(null, CancellationToken.None);
                    await _mojang.SetCape(parts[1], CancellationToken.None);
                    break;
                case "official" when parts.Length == 2 && _textHelper.IsValidId(parts[1]):
                    await _capeService.SetActive(new AxoCapeChoice(AxoCapeSource.Official, parts[1]), CancellationToken.None);
                    break;
                case "custom" when parts.Length == 2 && _textHelper.IsValidId(parts[1]):
                    await _capeService.SetActive(new AxoCapeChoice(AxoCapeSource.Custom, parts[1]), CancellationToken.None);
                    break;
                default:
                    throw new AxoException("errors.invalidInput");
            }
        }

        // ---------- Emotes ----------

        private async Task pushEmotes(string? onlyInstance)
        {
            var slots = await _emotes.Slots();
            var all = await _emotes.AllWithAnimations();
            var list = all.Select(imported => new { id = imported.Item.Id, name = imported.Item.Name, source = "imported", loop = imported.Item.Loop, moving = imported.Item.Moving }).ToList();
            foreach (var instance in targets(onlyInstance))
            {
                await _bridge.Send(instance, "emotes", new { slots, list });
                // Animationen einzeln: die Mod nimmt höchstens 256 KB je Nachricht an
                foreach (var (item, json) in all)
                {
                    await _bridge.Send(instance, "emoteAnim", new { id = item.Id, anim = json, animation = item.Animation });
                }
            }
        }

        private async Task sendEmote(string? emote)
        {
            if (emote != null && emote.StartsWith("c_", StringComparison.Ordinal))
            {
                if (await _emotes.Animation(emote) is { } animation)
                {
                    await _live.SendEmote(emote, animation.Json, animation.Animation);
                }

                return;
            }
            await _live.SendEmote(emote);
        }

        // ---------- Hilfen ----------

        private IEnumerable<string> targets(string? only) => only != null ? [only] : _bridge.ConnectedInstances;

        private InstanceInfo getInfo(Instance inst) =>
            new(inst.Id, inst.Name, inst.Minecraft.Version, inst.Loader.Type.ToString());

        private async Task<ServerStatus?> getPing(string address)
        {
            try
            {
                return await _serverPing.Ping(address, 3000);
            }
            catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException or TimeoutException or OperationCanceledException
                                           or AxoException or JsonException or InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>e4mc-Adressen (offene Welt über das Relais) erkennt man am Namen.</summary>
        public bool IsRelay(string address) =>
            _serverPing.ParseAddress(address).Host.EndsWith(".e4mc.link", StringComparison.OrdinalIgnoreCase);

        private string? firstLine(string? text) =>
            text?.Split('\n').Select(textLine => textLine.Trim()).FirstOrDefault(textLine => textLine.Length > 0) is { } line ? _textHelper.Clean(line, 40) : null;

        /// <summary>
        /// Passt die Server-Version (Text aus dem Ping, z. B. "Paper 1.21.4", "1.8.x-1.21.x") zur Minecraft-Version? <c>null</c> = unklar.
        /// </summary>
        internal bool? VersionFits(string? serverVersion, string minecraft)
        {
            if (string.IsNullOrWhiteSpace(serverVersion))
            {
                return null;
            }

            var found = versionNumber().Matches(serverVersion).Select(match => match.Value).ToList();
            if (found.Count == 0)
            {
                return null;
            }

            if (found.Any(version => compare(version, minecraft) == 0))
            {
                return true;
            }

            if (found.Count >= 2)
            {
                var low = found.MinBy(version => version, Comparer<string>.Create(compare))!;
                var high = found.MaxBy(version => version, Comparer<string>.Create(compare))!;
                return compare(minecraft, low) >= 0 && compare(minecraft, high) <= 0;
            }
            return false;
        }

        private string? versionText(string? serverVersion) =>
            serverVersion == null ? null : versionNumber().Matches(serverVersion).Select(match => match.Value).LastOrDefault();

        /// <summary>Vergleicht "1.21.4" mit "1.21.x" usw.; "x" passt auf alles.</summary>
        private int compare(string firstVersion, string secondVersion)
        {
            var firstParts = firstVersion.Split('.');
            var secondParts = secondVersion.Split('.');
            for (var index = 0; index < Math.Max(firstParts.Length, secondParts.Length); index++)
            {
                var firstPart = index < firstParts.Length ? firstParts[index] : "0";
                var secondPart = index < secondParts.Length ? secondParts[index] : "0";
                if (firstPart == "x" || secondPart == "x")
                {
                    return 0;
                }

                var comparison = (int.TryParse(firstPart, out var firstNumber) ? firstNumber : 0).CompareTo(int.TryParse(secondPart, out var secondNumber) ? secondNumber : 0);
                if (comparison != 0)
                {
                    return comparison;
                }
            }
            return 0;
        }

        private string uuid(JsonElement data) =>
            readString(data, "uuid") is { Length: 32 } candidateUuid && candidateUuid.All(Uri.IsHexDigit) ? candidateUuid.ToLowerInvariant() : throw new AxoException("errors.invalidInput");

        private string? readString(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var valueElement) && valueElement.ValueKind == JsonValueKind.String ? valueElement.GetString() : null;

        [GeneratedRegex(@"\d+\.\d+(?:\.(?:\d+|x))?")]
        private partial Regex versionNumber();

        [GeneratedRegex("^[A-Za-z0-9]{6,16}$")]
        private partial Regex projectIdPattern();
    }
}
