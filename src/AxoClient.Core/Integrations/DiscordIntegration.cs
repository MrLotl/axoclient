using System.Security.Cryptography;
using System.Text;
using AxoClient.Core.Common;
using DiscordRPC;
using DiscordRPC.Message;

namespace AxoClient.Core.Integrations;

/// <summary>
/// Discord-Status während des Spielens: Instanz, Server (außer im Streamer-Modus) und "Beitreten" über Discord.
/// </summary>
public sealed class DiscordIntegration : IDisposable
{
    private const string AppId = "1550925254124118067";
    private const string ImageKey = "axolotl";
    private const int MaxText = 128;

    private readonly SettingsService _settings;
    private readonly GameActivity _activity;
    private readonly Lock _lock = new();
    private DiscordRpcClient? _client;

    public DiscordIntegration(SettingsService settings, GameActivity activity)
    {
        _settings = settings;
        _activity = activity;
        activity.Changed += Update;
        settings.Changed += _ => Update();
    }

    /// <summary>Jemand hat in Discord auf "Beitreten" geklickt: Adresse und MC-Version.</summary>
    public event Action<string, string?>? JoinRequested;

    private void Update()
    {
        lock (_lock)
        {
            if (!_settings.Current.Integrations.DiscordRpc || _activity.InstanceName == null)
            {
                _client?.ClearPresence();
                if (!_settings.Current.Integrations.DiscordRpc)
                    Disconnect();
                return;
            }
            if (!Connect())
                return;
            try
            {
                var streamer = _settings.Current.Integrations.StreamerMode;
                var server = streamer ? null : _activity.JoinAddress;
                var presence = new RichPresence
                {
                    Details = Limit(_activity.InstanceName),
                    State = Limit(server != null ? $"Auf {server}" : $"Minecraft {_activity.Minecraft}"),
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
            catch (Exception ex)
            {
                AppLog.Warn("Discord-Status setzen", ex);
            }
        }
    }

    private bool Connect()
    {
        if (_client != null)
            return true;
        try
        {
            var client = new DiscordRpcClient(AppId);
            client.RegisterUriScheme();
            client.OnJoin += OnJoin;
            client.OnJoinRequested += (_, request) => client.Respond(request, true);
            client.Initialize();
            client.Subscribe(EventType.Join | EventType.JoinRequest);
            _client = client;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Discord-Verbindung aufbauen", ex);
            return false;
        }
    }

    private void OnJoin(object sender, JoinMessage args)
    {
        var parts = args.Secret.Split('\n', 2);
        if (parts[0].Length > 0 && Instances.ServerPing.IsValidAddress(parts[0]))
            JoinRequested?.Invoke(parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null);
    }

    private static string Limit(string text) => text.Length switch
    {
        < 2 => text.PadRight(2),
        > MaxText => text[..(MaxText - 3)] + "...",
        _ => text
    };

    private void Disconnect()
    {
        _client?.Dispose();
        _client = null;
    }

    public void Dispose()
    {
        lock (_lock)
            Disconnect();
    }
}
