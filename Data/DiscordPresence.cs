using System.Security.Cryptography;
using System.Text;
using DiscordRPC;
using DiscordRPC.Message;

namespace McLauncher;

/// <summary>
/// Discord-Status ("Spielt AxoClient") über Rich Presence, solange ein vom Launcher gestartetes Spiel läuft.
/// Auf einem Server bekommt der Status einen "Beitreten"-Knopf: Freunde, die darauf klicken, starten AxoClient
/// (Discord öffnet ihn über ein registriertes Protokoll) und landen auf demselben Server.
/// Der angezeigte Name und das Bild kommen von der Discord-Anwendung "AxoClient" (ID fest eingebaut).
/// </summary>
public sealed class DiscordPresence : IDisposable
{
    /// <summary>Name des Bildes, das in der Discord-Anwendung unter "Rich Presence → Art Assets" hochgeladen wurde.</summary>
    public const string ImageKey = "axolotl";

    private readonly object _lock = new();
    private DiscordRpcClient? _client;
    private string? _clientAppId;
    private int _runningGames;
    private Installation? _inst;
    private DateTime _startedAt;

    /// <summary>
    /// Jemand hat in Discord auf "Beitreten" geklickt: Server und Minecraft-Version.
    /// Wird im Hintergrund ausgelöst (nicht im UI-Thread).
    /// </summary>
    public event Action<string, string?>? JoinRequested;

    /// <summary>Beim Start des Launchers verbinden, damit ein "Beitreten" aus Discord ankommt.</summary>
    public void Initialize(LauncherSettings settings)
    {
        if (!settings.DiscordEnabled || string.IsNullOrWhiteSpace(settings.DiscordAppId))
            return;
        lock (_lock)
        {
            try
            {
                EnsureClient(settings.DiscordAppId.Trim());
            }
            catch
            {
                // Discord nicht installiert o.Ä.
            }
        }
    }

    /// <summary>Ein Spiel wurde gestartet: Status setzen (läuft bis <see cref="GameExited"/>).</summary>
    public void GameStarted(LauncherSettings settings, Installation inst, QuickPlay? quickPlay)
    {
        if (!settings.DiscordEnabled || string.IsNullOrWhiteSpace(settings.DiscordAppId))
            return;

        lock (_lock)
        {
            _runningGames++;
            _inst = inst;
            _startedAt = DateTime.UtcNow;
            try
            {
                EnsureClient(settings.DiscordAppId.Trim());
                SetPresence(quickPlay?.Server, quickPlay?.World != null ? "Einzelspieler" : null);
            }
            catch
            {
                // Discord läuft nicht oder die ID ist falsch: dann eben ohne Status spielen
            }
        }
    }

    /// <summary>Der Spieler hat einen Server betreten (Adresse) oder verlassen (null).</summary>
    public void ServerChanged(string? server)
    {
        lock (_lock)
        {
            if (_runningGames == 0 || _client == null || _inst == null)
                return;
            try
            {
                SetPresence(server, null);
            }
            catch
            {
                // Discord nicht verbunden
            }
        }
    }

    private void SetPresence(string? server, string? state)
    {
        var inst = _inst!;
        var presence = new RichPresence
        {
            Details = Limit(inst.Name),
            State = Limit(server != null ? $"Auf {server}" : state ?? $"{inst.Loader} {inst.MinecraftVersion}"),
            Timestamps = new Timestamps(_startedAt),
            Assets = new Assets { LargeImageKey = ImageKey, LargeImageText = "AxoClient" }
        };
        if (server != null)
        {
            // Für den "Beitreten"-Knopf verlangt Discord eine Gruppe und ein Geheimnis (hier: Server + Version)
            presence.Party = new Party
            {
                ID = "axo-" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(server)))[..16].ToLowerInvariant(),
                Size = 1,
                Max = 100,
                Privacy = Party.PrivacySetting.Public
            };
            var secret = server + "\n" + inst.MinecraftVersion;
            presence.Secrets = new Secrets { JoinSecret = secret.Length <= 128 ? secret : server[..Math.Min(server.Length, 128)] };
        }
        _client!.SetPresence(presence);
    }

    /// <summary>Ein Spiel wurde beendet: Status entfernen, wenn keins mehr läuft.</summary>
    public void GameExited()
    {
        lock (_lock)
        {
            _runningGames = Math.Max(0, _runningGames - 1);
            if (_runningGames == 0)
                _client?.ClearPresence();
        }
    }

    private void EnsureClient(string appId)
    {
        if (_client != null && _clientAppId == appId)
            return;
        _client?.Dispose();
        var client = new DiscordRpcClient(appId);
        // Protokoll "discord-<ID>://" registrieren, damit Discord AxoClient zum Beitreten starten kann
        client.RegisterUriScheme();
        client.OnJoin += OnJoin;
        client.OnJoinRequested += (_, request) => client.Respond(request, true); // anfragen können nur Discord-Freunde
        client.Initialize(); // verbindet sich im Hintergrund, auch wenn Discord erst später startet
        client.Subscribe(EventType.Join | EventType.JoinRequest);
        _client = client;
        _clientAppId = appId;
    }

    private void OnJoin(object sender, JoinMessage args)
    {
        var parts = args.Secret.Split('\n', 2);
        if (parts[0].Length > 0)
            JoinRequested?.Invoke(parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null);
    }

    /// <summary>Discord erlaubt höchstens 128 Zeichen und mindestens 2 pro Textzeile.</summary>
    private static string Limit(string text) => text.Length switch
    {
        < 2 => text.PadRight(2),
        > 128 => text[..125] + "...",
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
