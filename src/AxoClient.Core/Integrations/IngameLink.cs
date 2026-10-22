using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Core.Accounts;
using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;
using AxoClient.Core.Launch;
using AxoClient.Core.Skins;
using AxoClient.Core.Social;

namespace AxoClient.Core.Integrations;

/// <summary>
/// Das Axo-Menü im Spiel spricht über die <see cref="AxoModBridge"/> mit dem Launcher: Freunde und Anfragen, Einladen,
/// Nachjoinen (gleiche Instanz, andere Instanz, fehlende Mods), Capes, Emotes und kleine Einblendungen. Die Mod bekommt
/// nur fertige Daten, nie das Token des Online-Service.
/// </summary>
public sealed partial class IngameLink(
    AxoModBridge bridge,
    SocialService social,
    InboxManager inbox,
    OnlineClient online,
    CapeService capeService,
    CapeManager capes,
    MojangApi mojang,
    AccountManager accounts,
    SkinManager skins,
    InstanceManager instances,
    ServerListManager serverLists,
    ContentManager content,
    ModrinthProvider modrinth,
    GameLauncher games,
    GameActivity activity,
    LiveChannel live,
    EmoteLibrary emotes,
    ApiClient api,
    EventHub events,
    JobManager jobs)
{
    /// <summary>Größte Cape-Grafik, die an die Mod geht (Base64-Zeichen).</summary>
    private const int MaxImageChars = 180_000;

    private readonly ConcurrentDictionary<string, HashSet<string>> _sentSkins = new();
    private readonly ConcurrentDictionary<string, string> _invited = new();
    private readonly ConcurrentDictionary<string, JoinPlan> _plans = new();
    private readonly ConcurrentDictionary<string, string> _capeImages = new();
    private readonly SemaphoreSlim _skinLock = new(1, 1);
    private string? _hostedWorld;

    public void Start()
    {
        bridge.MessageReceived += (instance, type, data) => _ = HandleAsync(instance, type, data);
        events.Published += OnEvent;
        live.EmoteReceived += e => _ = bridge.SendAsync(null, "emote", new { uuid = e.Uuid, emote = e.Emote, anim = e.Anim, animation = e.Animation });
        activity.JoinAddressChanged += address =>
        {
            // Neuer Ort: alte Einladungen gelten nicht mehr
            _invited.Clear();
            _ = PushSocialAsync(null);
            _ = AnnounceWorldAsync(address);
        };
    }

    // ---------- Nachrichten der Mod ----------

    private async Task HandleAsync(string instance, string type, JsonElement data)
    {
        try
        {
            switch (type)
            {
                case "hello":
                case "retry":
                    _sentSkins.TryRemove(instance, out _);
                    await PushSocialAsync(instance);
                    await PushEmotesAsync(instance);
                    await PushCapesAsync(instance);
                    break;
                case "friendAccept":
                    await social.AcceptAsync(Uuid(data), CancellationToken.None);
                    break;
                case "friendDecline":
                    await social.DeclineAsync(Uuid(data), CancellationToken.None);
                    break;
                case "invite":
                    await InviteAsync(instance, Uuid(data));
                    break;
                case "inviteUndo":
                    await InviteUndoAsync(instance, Uuid(data));
                    break;
                case "inviteDecline":
                    await inbox.DiscardAsync(Str(data, "id") ?? "", CancellationToken.None);
                    break;
                case "joinCheck":
                    await bridge.SendAsync(instance, "joinPlan", await PlanAsync(instance, data));
                    break;
                case "join":
                    await JoinAsync(instance, data);
                    break;
                case "capeWear":
                    await WearCapeAsync(Str(data, "key") ?? "none");
                    await PushCapesAsync(null);
                    break;
                case "emoteSlots":
                    if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Array)
                        await emotes.SetSlotsAsync(slots.EnumerateArray().Select(s => s.ValueKind == JsonValueKind.String ? s.GetString() : null).ToList());
                    break;
                case "emote":
                    await SendEmoteAsync(Str(data, "emote"));
                    break;
                case "openLauncher":
                    events.Publish("app.focus");
                    break;
            }
        }
        catch (Exception ex) when (ex is AxoException or HttpRequestException or IOException or JsonException or InvalidOperationException
                                       or KeyNotFoundException or TaskCanceledException)
        {
            AppLog.Info($"Ingame-Menü: {type} fehlgeschlagen: {ex.Message}");
            var code = ex is AxoException axo ? axo.Text.Code : "errors.unknown";
            if (type == "join" || type == "joinCheck")
                await bridge.SendAsync(instance, "joinState", new { uuid = Str(data, "uuid"), phase = "error", code });
            else
                await bridge.SendAsync(instance, "error", new { action = type, code });
        }
    }

    private void OnEvent(string type, object? data)
    {
        switch (type)
        {
            case "social.changed":
                _ = PushSocialAsync(null);
                break;
            case "emotes.changed":
                _ = PushEmotesAsync(null);
                break;
            case "capes.changed":
            case "accounts.changed":
                _ = PushCapesAsync(null);
                break;
            case "notification.new" when data is NotificationItem item:
                _ = ToastAsync(item);
                break;
        }
    }

    // ---------- Freunde ----------

    private async Task PushSocialAsync(string? onlyInstance)
    {
        foreach (var instance in Targets(onlyInstance))
        {
            var friends = social.Friends;
            var invites = social.Inbox.Where(i => i.Kind == "invite" && i.Expires > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                .Select(i => new { id = i.Id, uuid = i.Sender.Uuid, name = i.Sender.Name, address = i.Title, created = i.Created })
                .ToList();
            await bridge.SendAsync(instance, "social", new
            {
                available = social.Available,
                me = social.Me == null ? null : new { uuid = social.Me.Uuid, name = social.Me.Name },
                here = await HereAsync(instance),
                invited = _invited.Keys.ToList(),
                invites,
                friends = friends.Select(f => new
                {
                    uuid = f.Uuid,
                    name = f.Name,
                    relation = f.Relation switch { Relation.Pending => "pending", Relation.Incoming => "incoming", _ => "friends" },
                    state = f.State switch { PresenceState.Ingame => "ingame", PresenceState.Launcher => "launcher", _ => "offline" },
                    since = f.Since,
                    join = f.Join,
                    joinKind = f.Join == null ? null : IsRelay(f.Join) ? "world" : "server"
                }).ToList()
            });
            // Auch der eigene Skin: das Menü zeigt damit die Cape-Vorschau
            var own = accounts.Active?.Uuid.Replace("-", "").ToLowerInvariant();
            _ = SendSkinsAsync(instance, friends.Select(f => f.Uuid).Concat(invites.Select(i => i.uuid))
                .Concat(own is { Length: 32 } ? [own] : []));
        }
    }

    /// <summary>Wo man gerade spielt: Adresse, Art und Name aus der Serverliste der Instanz (samt Symbol).</summary>
    private async Task<object?> HereAsync(string instanceId)
    {
        var address = activity.JoinAddress;
        if (address == null)
            return null;
        if (IsRelay(address))
            return new { address, kind = "world", name = (string?)null, icon = (string?)null };
        var normalized = ServerPing.Normalize(address);
        var entry = serverLists.List(instanceId).FirstOrDefault(s => ServerPing.IsValidAddress(s.Address) && ServerPing.Normalize(s.Address) == normalized);
        string? icon = null;
        if (entry != null && serverLists.Icon(instanceId, entry.Index) is { Length: > 0 and < 64 * 1024 } bytes)
            icon = Convert.ToBase64String(bytes);
        await Task.CompletedTask;
        return new { address, kind = "server", name = entry?.Name, icon };
    }

    private async Task SendSkinsAsync(string instance, IEnumerable<string> uuids)
    {
        var sent = _sentSkins.GetOrAdd(instance, _ => []);
        var todo = uuids.Distinct().ToList();
        await _skinLock.WaitAsync();
        try
        {
            foreach (var uuid in todo)
            {
                lock (sent)
                    if (!sent.Add(uuid))
                        continue;
                try
                {
                    if (await skins.PlayerSkinAsync(uuid, CancellationToken.None) is { Length: > 0 and < 96 * 1024 } png)
                        await bridge.SendAsync(instance, "skin", new { uuid, png = Convert.ToBase64String(png) });
                }
                catch (Exception ex) when (ex is HttpRequestException or AxoException or IOException or TaskCanceledException)
                {
                    lock (sent)
                        sent.Remove(uuid);
                }
            }
        }
        finally
        {
            _skinLock.Release();
        }
    }

    private async Task InviteAsync(string instance, string uuid)
    {
        var address = activity.JoinAddress ?? throw new AxoException("ingame.notOnServer");
        await inbox.SendInviteAsync(address, activity.InstanceName, activity.Minecraft, [uuid], CancellationToken.None);
        _invited[uuid] = address;
        await bridge.SendAsync(instance, "inviteState", new { uuid, invited = true });
    }

    private async Task InviteUndoAsync(string instance, string uuid)
    {
        await online.SendAsync(HttpMethod.Delete, $"/invites/{uuid}", ct: CancellationToken.None);
        _invited.TryRemove(uuid, out _);
        await bridge.SendAsync(instance, "inviteState", new { uuid, invited = false });
    }

    private async Task ToastAsync(NotificationItem item)
    {
        string P(string key) => item.Text.Params?.GetValueOrDefault(key)?.ToString() ?? "";
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
            return;
        var name = P("name");
        var uuid = social.Friends.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Uuid;
        await bridge.SendAsync(null, "toast", new
        {
            kind,
            name,
            uuid,
            server = P("server") is { Length: > 0 } s ? s : null,
            title = P("title") is { Length: > 0 } t ? t : null
        });
        if (uuid != null)
            foreach (var instance in bridge.ConnectedInstances)
                _ = SendSkinsAsync(instance, [uuid]);
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

    private async Task<object> PlanAsync(string instanceId, JsonElement data)
    {
        var plan = await BuildPlanAsync(instanceId, data);
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
            mods = plan.Mods.Select(m => new { name = m.Name, meta = m.Meta }).ToList(),
            version = plan.Version,
            reason = plan.Reason,
            here = activity.JoinAddress
        };
    }

    private async Task<JoinPlan> BuildPlanAsync(string instanceId, JsonElement data)
    {
        var uuid = Str(data, "uuid");
        var inviteId = Str(data, "inviteId");
        string? address = null;
        string? name = null;
        if (inviteId != null)
        {
            var invite = social.Inbox.FirstOrDefault(i => i.Id == inviteId) ?? throw new AxoException("ingame.inviteGone");
            name = invite.Sender.Name;
            uuid ??= invite.Sender.Uuid;
            var item = await inbox.FetchAsync(inviteId, CancellationToken.None);
            if (item.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.String)
            {
                using var doc = JsonDocument.Parse(payload.GetString()!);
                address = Str(doc.RootElement, "address");
            }
        }
        else if (uuid != null)
        {
            var friend = social.Friends.FirstOrDefault(f => f.Uuid == uuid) ?? throw new AxoException("ingame.friendGone");
            name = friend.Name;
            address = friend.Join;
        }
        if (address == null || !ServerPing.IsValidAddress(address))
            throw new AxoException("ingame.noJoin");

        var current = await instances.GetAsync(instanceId);
        var now = Info(current);
        var world = IsRelay(address);
        var key = inviteId ?? uuid ?? address;

        var pingTask = PingAsync(address);
        var worldTask = world ? live.PeekWorldAsync(address, TimeSpan.FromSeconds(2.5), CancellationToken.None) : Task.FromResult<JsonElement?>(null);
        await Task.WhenAll(pingTask, worldTask);
        var ping = await pingTask;
        var info = await worldTask;

        var hostVersion = info is { } i ? Str(i, "minecraft") : null;
        var hostLoader = info is { } j ? Str(j, "loader") : null;
        bool Fits(Instance inst)
        {
            if (hostVersion != null)
                return inst.Minecraft.Version == hostVersion
                       && (hostLoader == null || inst.Loader.Type.ToString().Equals(hostLoader, StringComparison.OrdinalIgnoreCase));
            return VersionFits(ping?.Version, inst.Minecraft.Version) != false;
        }

        var players = ping is { Online: true } ? $"{ping.Players:N0} / {ping.MaxPlayers:N0} Spieler".Replace(',', '.') : null;
        string title, meta;
        string? right, rightColor;
        if (world)
        {
            title = $"Offene Welt von {name}";
            meta = string.Join(" · ", new[] { "Offene Welt", hostVersion, hostLoader is { Length: > 0 } l ? char.ToUpperInvariant(l[0]) + l[1..] : null,
                ping is { Online: true } ? $"{ping.Players} Spieler" : null }.Where(s => s != null));
            right = "e4mc";
            rightColor = "#bdbdbd";
        }
        else
        {
            var normalized = ServerPing.Normalize(address);
            title = serverLists.List(instanceId).FirstOrDefault(s => ServerPing.IsValidAddress(s.Address) && ServerPing.Normalize(s.Address) == normalized)?.Name
                    ?? FirstLine(ping?.Motd) ?? address;
            meta = players == null ? address : $"{address} · {players}";
            right = ping?.LatencyMs is { } ms ? $"{ms} ms" : ping is { Online: false } ? "offline" : null;
            rightColor = ping?.LatencyMs is { } l2 ? l2 < 80 ? "#3fb950" : l2 < 150 ? "#bdbdbd" : "#e3b341" : "#e3b341";
        }

        if (!Fits(current))
        {
            // Andere Instanz nötig: erst eine, die den Server schon kennt, sonst die zuletzt gespielte passende
            var all = await instances.LoadAllAsync();
            var candidates = new List<(Instance Instance, bool KnowsServer, DateTimeOffset LastPlayed)>();
            foreach (var inst in all.Where(x => x.Id != instanceId && Fits(x)))
            {
                var knows = !world && serverLists.NormalizedAddresses(inst.Id).Contains(ServerPing.Normalize(address));
                var summary = await instances.SummaryAsync(inst);
                candidates.Add((inst, knows, summary.LastPlayed ?? inst.CreatedAt));
            }
            var best = candidates.OrderByDescending(c => c.KnowsServer).ThenByDescending(c => c.LastPlayed).FirstOrDefault();
            var version = hostVersion ?? VersionText(ping?.Version);
            if (best.Instance == null)
                return new JoinPlan(key, "none", address, uuid, name, inviteId, title, meta, right, rightColor, now, null, [], version, "noInstance");
            return new JoinPlan(key, "instance", address, uuid, name, inviteId, title, meta, right, rightColor, now, Info(best.Instance), [], version, null);
        }

        if (info is { } worldInfo && worldInfo.TryGetProperty("mods", out var modsElement) && modsElement.ValueKind == JsonValueKind.Array)
        {
            var missing = await MissingModsAsync(instanceId, current, modsElement);
            if (missing.Count > 0)
                return new JoinPlan(key, "mods", address, uuid, name, inviteId, title, meta, right, rightColor, now, null, missing, hostVersion, null);
        }
        return new JoinPlan(key, world ? "world" : "server", address, uuid, name, inviteId, title, meta, right, rightColor, now, null, [], null, null);
    }

    /// <summary>Mods des Gastgebers (Modrinth), die in der eigenen Instanz fehlen – ohne reine Client-Mods.</summary>
    private async Task<IReadOnlyList<MissingMod>> MissingModsAsync(string instanceId, Instance current, JsonElement mods)
    {
        var have = (await content.SyncAsync(instanceId)).Mods.Where(m => m.ProjectId != null).Select(m => m.ProjectId!).ToHashSet();
        var wanted = mods.EnumerateArray()
            .Where(m => m.ValueKind == JsonValueKind.Object && Str(m, "source") == "modrinth" && Str(m, "project") is { } p && ProjectId().IsMatch(p))
            .Select(m => new MissingMod(Text.Clean(Str(m, "name"), 60) is { Length: > 0 } n ? n : Str(m, "project")!, "modrinth", Str(m, "project")!,
                Str(m, "version") is { } v && ProjectId().IsMatch(v) ? v : null,
                $"{Text.Clean(Str(m, "modVersion"), 24)} · {current.Loader.Type}".TrimStart(' ', '·')))
            .Where(m => !have.Contains(m.ProjectId))
            .DistinctBy(m => m.ProjectId)
            .Take(60)
            .ToList();
        if (wanted.Count == 0)
            return [];
        try
        {
            var sides = await modrinth.ServerSidesAsync(wanted.Select(m => m.ProjectId), CancellationToken.None);
            return wanted.Where(m => sides.GetValueOrDefault(m.ProjectId) != "unsupported").ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or AxoException or JsonException or TaskCanceledException)
        {
            return wanted;
        }
    }

    private async Task JoinAsync(string instanceId, JsonElement data)
    {
        var key = Str(data, "inviteId") ?? Str(data, "uuid") ?? "";
        if (!_plans.TryGetValue(instanceId + "|" + key, out var plan))
            plan = await BuildPlanAsync(instanceId, data);
        var state = new { uuid = plan.Uuid, key = plan.Key };
        switch (plan.Kind)
        {
            case "server":
            case "world":
                await ConsumeInviteAsync(plan);
                await bridge.SendAsync(instanceId, "connect", new { address = plan.Address, name = plan.Title });
                await bridge.SendAsync(instanceId, "joinState", new { state.uuid, state.key, phase = "done" });
                break;
            case "instance" when plan.Next != null:
                await ConsumeInviteAsync(plan);
                await bridge.SendAsync(instanceId, "joinState", new { state.uuid, state.key, phase = "busy" });
                _ = RestartIntoAsync(instanceId, plan.Next.Id, plan.Address);
                break;
            case "mods":
                events.Publish("ingame.missingMods", new
                {
                    instanceId,
                    friend = plan.Name,
                    address = plan.Address,
                    title = plan.Title,
                    mods = plan.Mods.Select(m => new { name = m.Name, source = m.Source, projectId = m.ProjectId, versionId = m.VersionId, meta = m.Meta })
                });
                events.Publish("app.focus");
                await bridge.SendAsync(instanceId, "joinState", new { state.uuid, state.key, phase = "launcher" });
                break;
            default:
                events.Publish("app.focus");
                await bridge.SendAsync(instanceId, "joinState", new { state.uuid, state.key, phase = "launcher" });
                break;
        }
    }

    /// <summary>Eine angenommene Einladung aus dem Postfach nehmen.</summary>
    private async Task ConsumeInviteAsync(JoinPlan plan)
    {
        if (plan.InviteId != null)
            try
            {
                await inbox.DiscardAsync(plan.InviteId, CancellationToken.None);
            }
            catch (Exception ex) when (ex is AxoException or HttpRequestException)
            {
            }
    }

    /// <summary>Spiel sauber beenden lassen (die Mod schließt Minecraft), dann die andere Instanz direkt auf den Server starten.</summary>
    private async Task RestartIntoAsync(string runningId, string nextId, string address)
    {
        try
        {
            await bridge.SendAsync(runningId, "quit", new { });
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(25);
            while (games.IsRunning(runningId) && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(500);
            if (games.IsRunning(runningId))
                await games.StopAsync(runningId);
            await Task.Delay(500);
            await games.LaunchAsync(nextId, new QuickPlay(Server: address));
        }
        catch (Exception ex) when (ex is AxoException or IOException or InvalidOperationException)
        {
            AppLog.Info($"Nachjoinen mit anderer Instanz fehlgeschlagen: {ex.Message}");
            events.Publish("app.focus");
        }
    }

    /// <summary>
    /// Fehlende Mods installieren (aus dem Dialog im Launcher) und danach das Spiel neu starten, direkt in die Welt des Freundes.
    /// </summary>
    public Task InstallMissingAsync(string instanceId, string address, IReadOnlyList<(string Source, string ProjectId, string? VersionId)> mods) =>
        jobs.RunAsync("content.install", new TextRef("ingame.installingMissing"), async job =>
        {
            foreach (var (source, projectId, versionId) in mods)
            {
                if (!ProjectId().IsMatch(projectId) || versionId != null && !ProjectId().IsMatch(versionId))
                    continue;
                try
                {
                    await content.InstallAsync(instanceId, ContentType.Mod, new InstallRequest(source, projectId, versionId), job);
                }
                catch (AxoException) when (versionId != null)
                {
                    // Version des Gastgebers passt nicht: die beste für die Instanz
                    await content.InstallAsync(instanceId, ContentType.Mod, new InstallRequest(source, projectId), job);
                }
            }
            if (games.IsRunning(instanceId))
                _ = RestartIntoAsync(instanceId, instanceId, address);
            else
                await games.LaunchAsync(instanceId, new QuickPlay(Server: address));
        }, instanceId);

    /// <summary>Eigene offene Welt im Live-Raum bekannt machen (Version, Loader, Mods), damit Freunde vorab sehen, was fehlt.</summary>
    private async Task AnnounceWorldAsync(string? address)
    {
        try
        {
            if (address == null || !IsRelay(address))
            {
                if (_hostedWorld != null)
                {
                    _hostedWorld = null;
                    await live.SendWorldAsync(null);
                }
                return;
            }
            _hostedWorld = address;
            var running = games.Running.FirstOrDefault();
            if (running == null)
                return;
            var inst = await instances.GetAsync(running.InstanceId);
            var doc = await content.SyncAsync(inst.Id);
            var mods = doc.Mods.Where(m => m.Enabled && m.Source == ContentSource.Modrinth && m.ProjectId != null)
                .Select(m => new { name = m.Name, source = "modrinth", project = m.ProjectId, version = m.VersionId, modVersion = m.Version })
                .Take(300).ToList();
            await live.SendWorldAsync(new { minecraft = inst.Minecraft.Version, loader = inst.Loader.Type.ToString().ToLowerInvariant(), mods });
        }
        catch (Exception ex) when (ex is AxoException or IOException or JsonException)
        {
            AppLog.Info($"Welt-Infos nicht gesendet: {ex.Message}");
        }
    }

    // ---------- Capes ----------

    private async Task PushCapesAsync(string? onlyInstance)
    {
        var account = accounts.Active;
        if (account == null)
            return;
        var list = new List<object> { new { key = "none", name = "Kein Cape", kind = "none" } };
        var images = new List<(string Key, Func<Task<byte[]?>> Load)>();
        string? mojangActive = null;
        try
        {
            var profile = await mojang.GetOwnProfileAsync(CancellationToken.None);
            foreach (var cape in profile.Capes)
            {
                var key = "mojang:" + cape.Id;
                list.Add(new { key, name = cape.Alias, kind = "mojang" });
                if (cape.Active)
                    mojangActive = key;
                if (cape.Url is { } url)
                    images.Add((key, async () => await mojang.DownloadTextureAsync(url.Replace("http://", "https://"), CancellationToken.None)));
            }
        }
        catch (Exception ex) when (ex is AxoException or HttpRequestException or JsonException or TaskCanceledException)
        {
        }
        try
        {
            foreach (var cape in await capeService.OfficialAsync(CancellationToken.None))
            {
                var key = "official:" + cape.Id;
                list.Add(new { key, name = cape.Name, kind = "official" });
                images.Add((key, () => DownloadServiceImageAsync(cape.Url)));
            }
        }
        catch (Exception ex) when (ex is AxoException or HttpRequestException or JsonException or TaskCanceledException)
        {
        }
        foreach (var cape in (await capes.ListAsync()).Where(c => c.Status == CapeStatus.Approved))
        {
            var key = "custom:" + cape.Id;
            list.Add(new { key, name = cape.Name, kind = "custom" });
            images.Add((key, async () => await capes.ReadAsync(cape.Id, CancellationToken.None)));
        }
        var worn = account.Capes.Axo is { } axo
            ? (axo.Source == AxoCapeSource.Official ? "official:" : "custom:") + axo.Id
            : mojangActive ?? "none";

        foreach (var instance in Targets(onlyInstance))
        {
            await bridge.SendAsync(instance, "capes", new { worn, list });
            foreach (var (key, load) in images)
            {
                if (!_capeImages.TryGetValue(key, out var png))
                {
                    try
                    {
                        if (await load() is { Length: > 0 } bytes)
                            _capeImages[key] = png = Convert.ToBase64String(bytes);
                    }
                    catch (Exception ex) when (ex is AxoException or HttpRequestException or IOException or TaskCanceledException)
                    {
                    }
                }
                if (png is { Length: < MaxImageChars })
                    await bridge.SendAsync(instance, "capeImage", new { key, png });
            }
        }
    }

    private async Task<byte[]?> DownloadServiceImageAsync(string url)
    {
        // Nur Bilder vom eigenen Dienst
        var absolute = url.StartsWith('/') ? new Uri(new Uri(AppInfo.ServiceUrl), url) : new Uri(url);
        if (absolute.Host != new Uri(AppInfo.ServiceUrl).Host)
            return null;
        using var response = await api.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, absolute), CancellationToken.None);
        await ApiClient.EnsureSuccessAsync(response, CancellationToken.None);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task WearCapeAsync(string key)
    {
        var parts = key.Split(':', 2);
        switch (parts[0])
        {
            case "none":
                await capeService.SetActiveAsync(null, CancellationToken.None);
                await mojang.SetCapeAsync(null, CancellationToken.None);
                break;
            case "mojang" when parts.Length == 2:
                await capeService.SetActiveAsync(null, CancellationToken.None);
                await mojang.SetCapeAsync(parts[1], CancellationToken.None);
                break;
            case "official" when parts.Length == 2 && Text.IsValidId(parts[1]):
                await capeService.SetActiveAsync(new AxoCapeChoice(AxoCapeSource.Official, parts[1]), CancellationToken.None);
                break;
            case "custom" when parts.Length == 2 && Text.IsValidId(parts[1]):
                await capeService.SetActiveAsync(new AxoCapeChoice(AxoCapeSource.Custom, parts[1]), CancellationToken.None);
                break;
            default:
                throw new AxoException("errors.invalidInput");
        }
    }

    // ---------- Emotes ----------

    private async Task PushEmotesAsync(string? onlyInstance)
    {
        var slots = await emotes.SlotsAsync();
        var all = await emotes.AllWithAnimationsAsync();
        var list = all.Select(e => new { id = e.Item.Id, name = e.Item.Name, source = "imported" }).ToList();
        foreach (var instance in Targets(onlyInstance))
        {
            await bridge.SendAsync(instance, "emotes", new { slots, list });
            // Animationen einzeln: die Mod nimmt höchstens 256 KB je Nachricht an
            foreach (var (item, json) in all)
                await bridge.SendAsync(instance, "emoteAnim", new { id = item.Id, anim = json, animation = item.Animation });
        }
    }

    private async Task SendEmoteAsync(string? emote)
    {
        if (emote != null && emote.StartsWith("c_", StringComparison.Ordinal))
        {
            if (await emotes.AnimationAsync(emote) is { } anim)
                await live.SendEmoteAsync(emote, anim.Json, anim.Animation);
            return;
        }
        await live.SendEmoteAsync(emote);
    }

    // ---------- Hilfen ----------

    private IEnumerable<string> Targets(string? only) => only != null ? [only] : bridge.ConnectedInstances;

    private static InstanceInfo Info(Instance inst) =>
        new(inst.Id, inst.Name, inst.Minecraft.Version, inst.Loader.Type.ToString());

    private static async Task<ServerStatus?> PingAsync(string address)
    {
        try
        {
            return await ServerPing.PingAsync(address, 3000);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or TimeoutException or OperationCanceledException
                                       or AxoException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>e4mc-Adressen (offene Welt über das Relais) erkennt man am Namen.</summary>
    public static bool IsRelay(string address) =>
        ServerPing.ParseAddress(address).Host.EndsWith(".e4mc.link", StringComparison.OrdinalIgnoreCase);

    private static string? FirstLine(string? text) =>
        text?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) is { } line ? Text.Clean(line, 40) : null;

    /// <summary>
    /// Passt die Server-Version (Text aus dem Ping, z. B. "Paper 1.21.4", "1.8.x-1.21.x") zur Minecraft-Version? <c>null</c> = unklar.
    /// </summary>
    internal static bool? VersionFits(string? serverVersion, string minecraft)
    {
        if (string.IsNullOrWhiteSpace(serverVersion))
            return null;
        var found = VersionNumber().Matches(serverVersion).Select(m => m.Value).ToList();
        if (found.Count == 0)
            return null;
        if (found.Any(v => Compare(v, minecraft) == 0))
            return true;
        if (found.Count >= 2)
        {
            var low = found.MinBy(v => v, Comparer<string>.Create(Compare))!;
            var high = found.MaxBy(v => v, Comparer<string>.Create(Compare))!;
            return Compare(minecraft, low) >= 0 && Compare(minecraft, high) <= 0;
        }
        return false;
    }

    private static string? VersionText(string? serverVersion) =>
        serverVersion == null ? null : VersionNumber().Matches(serverVersion).Select(m => m.Value).LastOrDefault();

    /// <summary>Vergleicht "1.21.4" mit "1.21.x" usw.; "x" passt auf alles.</summary>
    private static int Compare(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length ? pa[i] : "0";
            var y = i < pb.Length ? pb[i] : "0";
            if (x == "x" || y == "x")
                return 0;
            var c = (int.TryParse(x, out var nx) ? nx : 0).CompareTo(int.TryParse(y, out var ny) ? ny : 0);
            if (c != 0)
                return c;
        }
        return 0;
    }

    private static string Uuid(JsonElement data) =>
        Str(data, "uuid") is { Length: 32 } u && u.All(Uri.IsHexDigit) ? u.ToLowerInvariant() : throw new AxoException("errors.invalidInput");

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [GeneratedRegex(@"\d+\.\d+(?:\.(?:\d+|x))?")]
    private static partial Regex VersionNumber();

    [GeneratedRegex("^[A-Za-z0-9]{6,16}$")]
    private static partial Regex ProjectId();
}
