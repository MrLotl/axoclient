using System.Security.Cryptography;
using System.Text;
using AxoClient.Common;
using AxoClient.DataModel.Settings;
using AxoClient.Instances.Contract;
using AxoClient.Integrations.Contract;
using AxoClient.Settings.Contract;
using DiscordRPC;
using DiscordRPC.Message;

namespace AxoClient.Integrations
{
    /// <summary>
    /// Discord-Status: im Launcher (je nach Einstellung) und während des Spielens mit Instanz, Server
    /// (außer im Streamer-Modus) und "Beitreten" über Discord.
    /// </summary>
    public sealed class DiscordIntegration : IDisposable, IDiscordIntegration
    {
        private readonly ISettingsService _settings;
        private readonly IGameActivity _activity;
        private readonly IAppLog _appLog;
        private readonly IServerPing _serverPing;
        private const string AppId = "1550925254124118067";
        private const string ImageKey = "axolotl";
        private const int MaxText = 128;

        private readonly Lock _lock = new();
        private readonly DateTimeOffset _launcherStartedAt = DateTimeOffset.Now;
        private DiscordRpcClient? _client;
        private bool _started;

        public DiscordIntegration(ISettingsService settings, IGameActivity activity, IAppLog appLog, IServerPing serverPing)
        {
            _settings = settings;
            _activity = activity;
            _appLog = appLog;
            _serverPing = serverPing;
            activity.Changed += update;
            settings.Changed += _ => update();
        }

        /// <summary>Jemand hat in Discord auf "Beitreten" geklickt: Adresse und MC-Version.</summary>
        public event Action<string, string?>? JoinRequested;

        /// <summary>Nach dem Laden der Einstellungen: Status im Launcher setzen, falls gewünscht.</summary>
        public void Start()
        {
            lock (_lock)
            {
                _started = true;
            }
            // Verbindung zu Discord im Hintergrund, damit der Launcher-Start nicht wartet
            _ = Task.Run(update);
        }

        private void update()
        {
            lock (_lock)
            {
                if (!_started)
                {
                    return;
                }

                var integrations = _settings.Current.Integrations;
                var playing = _activity.InstanceName != null;
                if (!integrations.DiscordRpc || (!playing && integrations.DiscordShow != DiscordShow.Always))
                {
                    _client?.ClearPresence();
                    if (!integrations.DiscordRpc)
                    {
                        disconnect();
                    }

                    return;
                }
                if (!connect())
                {
                    return;
                }

                if (!playing)
                {
                    setLauncherPresence();
                    return;
                }
                try
                {
                    var streamer = _settings.Current.Integrations.StreamerMode;
                    var server = streamer ? null : _activity.JoinAddress;
                    var presence = new RichPresence
                    {
                        Details = limit(_activity.InstanceName),
                        State = limit(server != null ? $"Auf {server}" : $"Minecraft {_activity.Minecraft}"),
                        Timestamps = new Timestamps((_activity.StartedAt ?? DateTimeOffset.Now).UtcDateTime),
                        Assets = new Assets { LargeImageKey = ImageKey, LargeImageText = AppInfo.Name }
                    };
                    if (server != null && _settings.Current.Social.AllowJoin)
                    {
                        presence.Party = new Party
                        {
                            ID = "axo-" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(server)))[..16].ToLowerInvariant(),
                            Size = 1,
                            Max = 100,
                            Privacy = Party.PrivacySetting.Public
                        };
                        var secret = server + "\n" + _activity.Minecraft;
                        presence.Secrets = new Secrets { JoinSecret = secret.Length <= MaxText ? secret : server[..Math.Min(server.Length, MaxText)] };
                    }
                    _client!.SetPresence(presence);
                }
                catch (Exception exception)
                {
                    _appLog.Warn("Discord-Status setzen", exception);
                }
            }
        }

        private void setLauncherPresence()
        {
            try
            {
                _client!.SetPresence(new RichPresence
                {
                    Details = "Im Launcher",
                    Timestamps = new Timestamps(_launcherStartedAt.UtcDateTime),
                    Assets = new Assets { LargeImageKey = ImageKey, LargeImageText = AppInfo.Name }
                });
            }
            catch (Exception exception)
            {
                _appLog.Warn("Discord-Status setzen", exception);
            }
        }

        private bool connect()
        {
            if (_client != null)
            {
                return true;
            }

            try
            {
                var client = new DiscordRpcClient(AppId);
                client.RegisterUriScheme();
                client.OnJoin += onJoin;
                client.OnJoinRequested += (_, request) => client.Respond(request, true);
                client.Initialize();
                client.Subscribe(EventType.Join | EventType.JoinRequest);
                _client = client;
                return true;
            }
            catch (Exception exception)
            {
                _appLog.Warn("Discord-Verbindung aufbauen", exception);
                return false;
            }
        }

        private void onJoin(object sender, JoinMessage arguments)
        {
            var parts = arguments.Secret.Split('\n', 2);
            if (parts[0].Length > 0 && _serverPing.IsValidAddress(parts[0]))
            {
                JoinRequested?.Invoke(parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null);
            }
        }

        private string limit(string text) => text.Length switch
        {
            < 2 => text.PadRight(2),
            > MaxText => text[..(MaxText - 3)] + "...",
            _ => text
        };

        private void disconnect()
        {
            _client?.Dispose();
            _client = null;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                disconnect();
            }
        }
    }
}
