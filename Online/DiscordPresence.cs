using System.Security.Cryptography;
using System.Text;
using DiscordRPC;
using DiscordRPC.Message;

namespace AxoClient.Online;

public sealed class DiscordPresence : IDisposable
{
    private const string ImageKey = "axolotl";
    private const int MaxText = 128;

    private readonly object _lock = new();
    private DiscordRpcClient? _client;
    private int _runningGames;
    private Installation? _inst;
    private DateTime _startedAt;

    public event Action<string, string?>? JoinRequested;

    public void Initialize(LauncherSettings settings)
    {
        if (!settings.DiscordEnabled)
            return;
        lock (_lock)
            TryConnect("Discord-Verbindung aufbauen");
    }

    public void GameStarted(LauncherSettings settings, Installation inst, QuickPlay? quickPlay)
    {
        if (!settings.DiscordEnabled)
            return;
        lock (_lock)
        {
            _runningGames++;
            _inst = inst;
            _startedAt = DateTime.UtcNow;
            if (TryConnect("Discord-Status setzen"))
                TrySetPresence(quickPlay?.Server, quickPlay?.World != null ? "Einzelspieler" : null);
        }
    }

    public void ServerChanged(string? server)
    {
        lock (_lock)
        {
            if (_runningGames > 0 && _client != null && _inst != null)
                TrySetPresence(server, null);
        }
    }

    public void GameExited()
    {
        lock (_lock)
        {
            _runningGames = Math.Max(0, _runningGames - 1);
            if (_runningGames == 0)
                _client?.ClearPresence();
        }
    }

    private bool TryConnect(string context)
    {
        try
        {
            if (_client != null)
                return true;
            var client = new DiscordRpcClient(AppInfo.DiscordAppId);
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
            ErrorReport.Log(context, ex);
            return false;
        }
    }

    private void TrySetPresence(string? server, string? state)
    {
        try
        {
            var inst = _inst!;
            var presence = new RichPresence
            {
                Details = Limit(inst.Name),
                State = Limit(server != null ? $"Auf {server}" : state ?? $"{inst.Loader} {inst.MinecraftVersion}"),
                Timestamps = new Timestamps(_startedAt),
                Assets = new Assets { LargeImageKey = ImageKey, LargeImageText = AppInfo.Name }
            };
            if (server != null)
            {
                presence.Party = new Party
                {
                    ID = "axo-" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(server)))[..16].ToLowerInvariant(),
                    Size = 1,
                    Max = 100,
                    Privacy = Party.PrivacySetting.Public
                };
                var secret = server + "\n" + inst.MinecraftVersion;
                presence.Secrets = new Secrets
                {
                    JoinSecret = secret.Length <= MaxText ? secret : server[..Math.Min(server.Length, MaxText)]
                };
            }
            _client!.SetPresence(presence);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Discord-Status setzen", ex);
        }
    }

    private void OnJoin(object sender, JoinMessage args)
    {
        var parts = args.Secret.Split('\n', 2);
        if (parts[0].Length > 0)
            JoinRequested?.Invoke(parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null);
    }

    private static string Limit(string text) => text.Length switch
    {
        < 2 => text.PadRight(2),
        > MaxText => text[..(MaxText - 3)] + "...",
        _ => text
    };

    public void Dispose()
    {
        lock (_lock)
        {
            _client?.Dispose();
            _client = null;
        }
    }
}
