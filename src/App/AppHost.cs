using System.Text.Json;
using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Instances;
using AxoClient.DataModel.Social;
using AxoClient.Instances.Contract;
using AxoClient.Integrations.Contract;
using AxoClient.Settings.Contract;
using AxoClient.Social.Contract;
using AxoClient.Updates.Contract;

namespace AxoClient.App
{
    /// <summary>Startet die Dienste des Launchers, verbindet ihre Ereignisse und räumt beim Beenden auf.</summary>
    public sealed class AppHost : IAsyncDisposable
    {
        private readonly AppPaths _paths;
        private readonly IEventHub _events;
        private readonly ISettingsService _settings;
        private readonly IAccountManager _accounts;
        private readonly IDiscordIntegration _discord;
        private readonly IAxoModBridge _bridge;
        private readonly IIngameLink _ingame;
        private readonly ISocialService _social;
        private readonly IUpdateService _launcherUpdates;
        private readonly IInstanceManager _instances;
        private readonly IUpdateScanner _updates;
        private readonly INotificationManager _notifications;
        private readonly IModrinthProvider _modrinth;
        private readonly ICapeService _capeService;
        private readonly IJsonHelper _jsonHelper;
        private readonly IAppLog _appLog;
        private readonly ISystemInfo _systemInfo;

        public AppHost(AppPaths paths, IEventHub events, ISettingsService settings, IAccountManager accounts, IDiscordIntegration discord,
            IAxoModBridge bridge, IIngameLink ingame, ISocialService social, IUpdateService launcherUpdates, IInstanceManager instances,
            IUpdateScanner updates, INotificationManager notifications, IModrinthProvider modrinth, ICapeService capeService,
            IGameActivity playActivity, ILiveChannel live, SocialHookProxy socialHook, IJsonHelper jsonHelper, IAppLog appLog,
            ISystemInfo systemInfo)
        {
            _paths = paths;
            _events = events;
            _settings = settings;
            _accounts = accounts;
            _discord = discord;
            _bridge = bridge;
            _ingame = ingame;
            _social = social;
            _launcherUpdates = launcherUpdates;
            _instances = instances;
            _updates = updates;
            _notifications = notifications;
            _modrinth = modrinth;
            _capeService = capeService;
            _jsonHelper = jsonHelper;
            _appLog = appLog;
            _systemInfo = systemInfo;

            // Der SocialService braucht den GameLauncher und umgekehrt; der Stellvertreter löst den Kreis
            socialHook.Target = social;
            playActivity.JoinAddressChanged += address => social.SetJoinAddress(address);
            // Laufende Spiele zeigen das neue Axo-Cape sofort, ohne auf die nächste Abfrage beim Dienst zu warten
            capeService.ActiveChanged += (uuid, cape) => _ = bridge.Send(null, "cape", new { uuid, cape });
            // Ergebnis einer Cape-Prüfung (Benachrichtigung vom Dienst) in die eigene Cape-Liste übernehmen
            social.RemoteNotification += notification => _ = applyCapeReview(notification);
            // Emotes: Mod → Launcher → Dienst (Raum des Servers) → andere Launcher → deren Mod
            playActivity.JoinAddressChanged += address => live.SetServer(address);
            // Lokale Benachrichtigungen aus Core-Ereignissen
            events.Published += onEvent;
        }

        public async Task Start()
        {
            await _settings.Load();
            await _accounts.Load();
            _discord.Start();
            _bridge.Start();
            _ingame.Start();
            _social.Start();
            _launcherUpdates.StartBackgroundChecks();
            _ = dailyModUpdateCheck();
            _ = watchDiskSpace();
        }

        public async ValueTask DisposeAsync()
        {
            _social.Stop();
            _launcherUpdates.ApplyOnExitIfReady();
            _discord.Dispose();
            await _bridge.DisposeAsync();
        }

        /// <summary>Core-Ereignis "notify" → Glocke (mit den Regeln aus den Einstellungen).</summary>
        private void onEvent(string type, object? data)
        {
            if (type != "notify" || data == null)
            {
                return;
            }
            try
            {
                var json = JsonSerializer.SerializeToElement(data, _jsonHelper.Options);
                var kind = json.GetProperty("type").GetString() ?? "hint";
                var text = JsonSerializer.Deserialize<TextRef>(json.GetProperty("text").GetRawText(), _jsonHelper.Options)!;
                if (kind == "hint")
                {
                    _events.Publish("toast", new { text });
                    return;
                }
                _notifications.Add(kind, text);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                _appLog.Warn("Benachrichtigung konnte nicht gelesen werden", exception);
            }
        }

        /// <summary>"capeReview" vom Dienst: Status des eigenen Capes (freigegeben/abgelehnt, Grund) übernehmen.</summary>
        private async Task applyCapeReview(ServiceNotification notification)
        {
            if (notification.Kind != "capeReview" || notification.Data == null)
            {
                return;
            }
            try
            {
                var data = JsonSerializer.SerializeToElement(notification.Data, _jsonHelper.Options);
                var localId = data.TryGetProperty("localId", out var localIdElement) ? localIdElement.GetString() : null;
                var approved = data.TryGetProperty("approved", out var approvedElement) && approvedElement.ValueKind == JsonValueKind.True;
                var reason = data.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String ? reasonElement.GetString() : null;
                if (localId != null)
                {
                    await _capeService.ApplyReview(localId, approved, reason, CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException or AxoException)
            {
                _appLog.Warn("Cape-Prüfung konnte nicht übernommen werden", exception);
            }
        }

        /// <summary>Mod- und Modpack-Updates höchstens einmal am Tag, gesammelt pro Instanz.</summary>
        private async Task dailyModUpdateCheck()
        {
            await Task.Delay(TimeSpan.FromMinutes(2));
            while (true)
            {
                try
                {
                    foreach (var instance in await _instances.LoadAll())
                    {
                        await checkUpdates(instance);
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    _appLog.Warn("Tägliche Update-Prüfung fehlgeschlagen", exception);
                }
                await Task.Delay(TimeSpan.FromHours(24));
            }
        }

        private async Task checkUpdates(Instance instance)
        {
            var updates = await _updates.Scan(instance.Id, CancellationToken.None);
            if (updates.Count > 0)
            {
                _notifications.Add("modUpdates", new TextRef("notifications.modUpdates", ("count", updates.Count), ("instance", instance.Name)),
                    new() { ["instanceId"] = instance.Id }, $"modUpdates:{instance.Id}");
            }
            if (instance.Origin is { Type: OriginType.Modpack, Source: "modrinth", ProjectId: { } processId, VersionId: { } vid })
            {
                var latest = (await _modrinth.GetVersions(processId, null, null, ProjectKind.Modpack, CancellationToken.None)).FirstOrDefault();
                if (latest != null && latest.Id != vid)
                {
                    _notifications.Add("modpackUpdates", new TextRef("notifications.modpackUpdate", ("instance", instance.Name),
                        ("version", latest.VersionNumber)), new() { ["instanceId"] = instance.Id }, $"modpack:{instance.Id}");
                }
            }
        }

        private async Task watchDiskSpace()
        {
            while (true)
            {
                var free = _systemInfo.FreeDiskBytes(_paths.Root);
                if (free < 5L * 1024 * 1024 * 1024)
                {
                    _notifications.Add("lowDiskSpace", new TextRef("notifications.lowDiskSpace", ("freeGb", free / 1024 / 1024 / 1024)),
                        dedupeKey: "lowDisk");
                }
                await Task.Delay(TimeSpan.FromHours(1));
            }
        }
    }
}
