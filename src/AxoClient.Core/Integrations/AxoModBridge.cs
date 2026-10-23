using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AxoClient.Core.Common;
using AxoClient.Core.Launch;

namespace AxoClient.Core.Integrations;

/// <summary>
/// Lokale WebSocket-Verbindung zur AxoMod, nur auf 127.0.0.1. Der Launcher übergibt Port und ein zufälliges
/// Einmal-Token per JVM-Argument (<c>-Daxoclient.ipc</c>, <c>-Daxoclient.token</c>). Freunde, Einladungen und
/// Benachrichtigungen laufen über den Launcher; die Mod bekommt nie das Token des Online-Service.
/// </summary>
public sealed class AxoModBridge : IGameLaunchHook, IAsyncDisposable
{
    /// <summary>Funktionen, die der Launcher der Mod per "welcome" anbietet.</summary>
    private static readonly string[] BaseFeatures = ["emotes", "menu", "social", "capes", "customEmotes", "join", "toasts"];

    /// <summary>
    /// Ob es ein Overlay-Fenster gibt, das die "hud"-Nachrichten der Mod (private Widgets) zeichnet. Setzt die App beim
    /// Start; nur dann bietet der Launcher "privateOverlay" an.
    /// </summary>
    public static bool PrivateOverlay { get; set; }

    private static string[] Features => PrivateOverlay ? [.. BaseFeatures, "privateOverlay"] : BaseFeatures;

    /// <summary>Größte Nachricht der Mod (Bilder des privaten Overlays können ein paar hundert KB groß sein).</summary>
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly ConcurrentDictionary<string, string> _tokens = new();
    private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>Nachricht von der Mod: (Instanz-ID, Typ, Daten).</summary>
    public event Action<string, string, JsonElement>? MessageReceived;

    public int Port { get; private set; }

    public void Start()
    {
        if (_listener != null)
            return;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);
    }

    IEnumerable<string> IGameLaunchHook.JvmArguments(LaunchContext ctx)
    {
        if (!ctx.Instance.AxoMod || _listener == null)
            return [];
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        _tokens[ctx.Instance.Id] = token;
        return
        [
            $"-Daxoclient.ipc=127.0.0.1:{Port}",
            $"-Daxoclient.token={token}",
            $"-Daxoclient.instance={ctx.Instance.Id}",
            $"-Daxoclient.api={AppInfo.ServiceUrl}/v2"
        ];
    }

    void IGameLaunchHook.OnExited(LaunchContext ctx, RunningProcess process, int exitCode, bool crashed)
    {
        _tokens.TryRemove(ctx.Instance.Id, out _);
        if (_sockets.TryRemove(ctx.Instance.Id, out var socket))
            socket.Abort();
    }

    /// <summary>Nachricht an die Mod einer Instanz (oder aller verbundenen Mods).</summary>
    public async Task SendAsync(string? instanceId, string type, object? data)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, data }, Json.Compact));
        // Ein WebSocket erlaubt nur eine Sendung zur selben Zeit
        await _sendLock.WaitAsync();
        try
        {
            foreach (var (id, socket) in _sockets)
            {
                if (instanceId != null && id != instanceId || socket.State != WebSocketState.Open)
                    continue;
                try
                {
                    // Hängt eine Mod, soll sie die anderen nicht aufhalten
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
                }
                catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
                {
                }
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Instanzen, deren Mod gerade verbunden ist.</summary>
    public IReadOnlyList<string> ConnectedInstances => _sockets.Where(s => s.Value.State == WebSocketState.Open).Select(s => s.Key).ToList();

    public bool IsConnected(string instanceId) => _sockets.TryGetValue(instanceId, out var s) && s.State == WebSocketState.Open;

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var tcp = client;
        var stream = client.GetStream();
        try
        {
            // HTTP-Upgrade lesen (höchstens 8 KB)
            var request = await ReadHeadersAsync(stream, ct);
            var key = Header(request, "Sec-WebSocket-Key");
            var path = request.Split("\r\n")[0].Split(' ').ElementAtOrDefault(1) ?? "";
            var query = path.Contains('?') ? System.Web.HttpUtility.ParseQueryString(path[(path.IndexOf('?') + 1)..]) : null;
            var instance = query?["instance"];
            var token = query?["token"];
            if (key == null || instance == null || token == null || !_tokens.TryGetValue(instance, out var expected)
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
            {
                await stream.WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), ct);
                return;
            }

            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n"), ct);

            using var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, TimeSpan.FromSeconds(20));
            _sockets[instance] = socket;
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            var tooLarge = false;
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                // Teilstücke zusammensetzen; zu große Nachrichten verwerfen
                if (!tooLarge)
                {
                    if (message.Length + result.Count > MaxMessageBytes)
                        tooLarge = true;
                    else
                        await message.WriteAsync(buffer.AsMemory(0, result.Count), ct);
                }
                if (!result.EndOfMessage)
                    continue;
                var skip = tooLarge || result.MessageType != WebSocketMessageType.Text;
                var length = (int)message.Length;
                message.SetLength(0);
                tooLarge = false;
                if (skip)
                    continue;
                try
                {
                    using var doc = JsonDocument.Parse(message.GetBuffer().AsMemory(0, length));
                    var type = doc.RootElement.GetProperty("type").GetString();
                    if (type == "hello")
                        await SendAsync(instance, "welcome", new { protocol = 2, features = Features });
                    if (type is { Length: > 0 and < 40 })
                        MessageReceived?.Invoke(instance, type, doc.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default);
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
                {
                }
            }
            _sockets.TryRemove(instance, out _);
        }
        catch (Exception ex) when (ex is IOException or WebSocketException or OperationCanceledException)
        {
        }
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
                break;
            total += read;
            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n"))
                break;
        }
        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static string? Header(string request, string name) =>
        request.Split("\r\n").Select(l => l.Split(':', 2)).FirstOrDefault(p => p.Length == 2 && p[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            ?[1].Trim();

    public async ValueTask DisposeAsync()
    {
        if (_cts != null)
            await _cts.CancelAsync();
        _listener?.Stop();
        foreach (var socket in _sockets.Values)
            socket.Abort();

    }
}
