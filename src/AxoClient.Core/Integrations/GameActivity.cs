using System.Text.RegularExpressions;
using AxoClient.Core.Common;
using AxoClient.Core.Launch;

namespace AxoClient.Core.Integrations;

/// <summary>
/// Liest aus dem Spiel-Log, wo gerade gespielt wird: Server-Verbindung ("Connecting to host, port") und
/// e4mc-Adresse einer geöffneten Welt ("Domain assigned: …"). Meldet Änderungen an Online-Status und Discord.
/// </summary>
public sealed partial class GameActivity(SettingsService settings) : IGameLaunchHook
{
    private string? _server;
    private string? _relay;

    /// <summary>Aktuelle Adresse zum Nachjoinen (Server oder e4mc), <c>null</c> = Einzelspieler/Menü.</summary>
    public string? JoinAddress => _server ?? _relay;
    public string? InstanceName { get; private set; }
    public string? Minecraft { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }

    public event Action<string?>? JoinAddressChanged;
    public event Action? Changed;

    void IGameLaunchHook.OnStarted(LaunchContext ctx, RunningProcess process)
    {
        _server = ctx.QuickPlay?.Server;
        _relay = null;
        InstanceName = ctx.Instance.Name;
        Minecraft = ctx.Instance.Minecraft.Version;
        StartedAt = DateTimeOffset.Now;
        process.LineReceived += OnLine;
        Raise();
    }

    void IGameLaunchHook.OnExited(LaunchContext ctx, RunningProcess process, int exitCode, bool crashed)
    {
        process.LineReceived -= OnLine;
        _server = null;
        _relay = null;
        InstanceName = null;
        StartedAt = null;
        Raise();
    }

    private void OnLine(LogLine line)
    {
        var text = line.Text;
        if (Connecting().Match(text) is { Success: true } connect)
        {
            var port = int.TryParse(connect.Groups[2].Value, out var p) ? p : 25565;
            _server = port == 25565 ? connect.Groups[1].Value : $"{connect.Groups[1].Value}:{port}";
            Raise();
        }
        else if (text.Contains("Disconnecting from server", StringComparison.Ordinal) || text.Contains("Stopping singleplayer server")
                 || text.Contains("[CHAT] Disconnected", StringComparison.Ordinal))
        {
            if (_server != null || _relay != null)
            {
                _server = null;
                _relay = null;
                Raise();
            }
        }
        else if (settings.Current.Integrations.FriendJoinRelay && DomainAssigned().Match(text) is { Success: true } relay)
        {
            _relay = relay.Groups[1].Value;
            Raise();
        }
    }

    private void Raise()
    {
        JoinAddressChanged?.Invoke(JoinAddress);
        Changed?.Invoke();
    }

    [GeneratedRegex(@"Connecting to ([A-Za-z0-9.\-]+), (\d{1,5})")]
    private static partial Regex Connecting();

    [GeneratedRegex(@"Domain assigned: ([A-Za-z0-9.\-]+\.[A-Za-z]{2,})")]
    private static partial Regex DomainAssigned();
}
