using System.Text.RegularExpressions;
using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.Integrations.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Settings.Contract;

namespace AxoClient.Integrations
{
    /// <summary>
    /// Liest aus dem Spiel-Log, wo gerade gespielt wird: Server-Verbindung ("Connecting to host, port") und
    /// e4mc-Adresse einer geöffneten Welt ("Domain assigned: …"). Meldet Änderungen an Online-Status und Discord.
    /// </summary>
    public sealed partial class GameActivity : IGameLaunchHook, IGameActivity
    {
        private readonly ISettingsService _settings;
        private string? _server;
        private string? _relay;

        /// <summary>Aktuelle Adresse zum Nachjoinen (Server oder e4mc), <c>null</c> = Einzelspieler/Menü.</summary>
        public string? JoinAddress => _server ?? _relay;
        public string? InstanceName { get; private set; }
        public string? Minecraft { get; private set; }
        public DateTimeOffset? StartedAt { get; private set; }

        public event Action<string?>? JoinAddressChanged;
        public event Action? Changed;

        public GameActivity(ISettingsService settings)
        {
            _settings = settings;
        }

        void IGameLaunchHook.OnStarted(LaunchContext context, RunningProcess process)
        {
            _server = context.QuickPlay?.Server;
            _relay = null;
            InstanceName = context.Instance.Name;
            Minecraft = context.Instance.Minecraft.Version;
            StartedAt = DateTimeOffset.Now;
            process.LineReceived += onLine;
            raise();
        }

        void IGameLaunchHook.OnExited(LaunchContext context, RunningProcess process, int exitCode, bool crashed)
        {
            process.LineReceived -= onLine;
            _server = null;
            _relay = null;
            InstanceName = null;
            StartedAt = null;
            raise();
        }

        private void onLine(LogLine line)
        {
            var text = line.Text;
            if (connecting().Match(text) is { Success: true } connect)
            {
                var port = int.TryParse(connect.Groups[2].Value, out var parsed) ? parsed : 25565;
                _server = port == 25565 ? connect.Groups[1].Value : $"{connect.Groups[1].Value}:{port}";
                raise();
            }
            else if (text.Contains("Disconnecting from server", StringComparison.Ordinal) || text.Contains("Stopping singleplayer server")
                     || text.Contains("[CHAT] Disconnected", StringComparison.Ordinal))
            {
                if (_server != null || _relay != null)
                {
                    _server = null;
                    _relay = null;
                    raise();
                }
            }
            else if (_settings.Current.Integrations.FriendJoinRelay && domainAssigned().Match(text) is { Success: true } relay)
            {
                _relay = relay.Groups[1].Value;
                raise();
            }
        }

        private void raise()
        {
            JoinAddressChanged?.Invoke(JoinAddress);
            Changed?.Invoke();
        }

        [GeneratedRegex(@"Connecting to ([A-Za-z0-9.\-]+), (\d{1,5})")]
        private partial Regex connecting();

        [GeneratedRegex(@"Domain assigned: ([A-Za-z0-9.\-]+\.[A-Za-z]{2,})")]
        private partial Regex domainAssigned();
    }
}
