using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.Integrations.Contract;
using AxoClient.Launch.Contract;

namespace AxoClient.Integrations
{
    /// <summary>
    /// Lokale WebSocket-Verbindung zur AxoMod, nur auf 127.0.0.1. Der Launcher übergibt Port und ein zufälliges
    /// Einmal-Token per JVM-Argument (<c>-Daxoclient.ipc</c>, <c>-Daxoclient.token</c>). Freunde, Einladungen und
    /// Benachrichtigungen laufen über den Launcher; die Mod bekommt nie das Token des Online-Service.
    /// </summary>
    public sealed class AxoModBridge : IGameLaunchHook, IAsyncDisposable, IAxoModBridge
    {
        private readonly IJsonHelper _jsonHelper;
        private readonly IAppInfo _appInfo;
        /// <summary>Funktionen, die der Launcher der Mod per "welcome" anbietet.</summary>
        private readonly string[] _baseFeatures = ["emotes", "menu", "social", "capes", "customEmotes", "join", "toasts", "blocks"];

        /// <summary>
        /// Ob es ein Overlay-Fenster gibt, das die "hud"-Nachrichten der Mod (private Widgets) zeichnet. Setzt die App beim
        /// Start; nur dann bietet der Launcher "privateOverlay" an.
        /// </summary>
        public bool PrivateOverlay { get; set; }

        private string[] Features => PrivateOverlay ? [.. _baseFeatures, "privateOverlay"] : _baseFeatures;

        /// <summary>Größte Nachricht der Mod (Bilder des privaten Overlays können ein paar hundert KB groß sein).</summary>
        private const int MaxMessageBytes = 1024 * 1024;

        private readonly ConcurrentDictionary<string, string> _tokens = new();
        private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;

        /// <summary>Nachricht von der Mod: (Instanz-ID, Typ, Daten).</summary>
        public event Action<string, string, JsonElement>? MessageReceived;

        public AxoModBridge(IJsonHelper jsonHelper, IAppInfo appInfo)
        {
            _jsonHelper = jsonHelper;
            _appInfo = appInfo;
        }

        public int Port { get; private set; }

        public void Start()
        {
            if (_listener != null)
            {
                return;
            }

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();
            _ = acceptLoop(_cts.Token);
        }

        IEnumerable<string> IGameLaunchHook.JvmArguments(LaunchContext context)
        {
            if (!context.Instance.AxoMod || _listener == null)
            {
                return [];
            }

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            _tokens[context.Instance.Id] = token;
            return
            [
                $"-Daxoclient.ipc=127.0.0.1:{Port}",
                $"-Daxoclient.token={token}",
                $"-Daxoclient.instance={context.Instance.Id}",
                $"-Daxoclient.api={_appInfo.ServiceUrl}/v2"
            ];
        }

        void IGameLaunchHook.OnExited(LaunchContext context, RunningProcess process, int exitCode, bool crashed)
        {
            _tokens.TryRemove(context.Instance.Id, out _);
            if (_sockets.TryRemove(context.Instance.Id, out var socket))
            {
                socket.Abort();
            }
        }

        /// <summary>Nachricht an die Mod einer Instanz (oder aller verbundenen Mods).</summary>
        public async Task Send(string? instanceId, string type, object? data)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, data }, _jsonHelper.Compact));
            // Ein WebSocket erlaubt nur eine Sendung zur selben Zeit
            await _sendLock.WaitAsync();
            try
            {
                foreach (var (id, socket) in _sockets)
                {
                    if (instanceId != null && id != instanceId || socket.State != WebSocketState.Open)
                    {
                        continue;
                    }

                    try
                    {
                        // Hängt eine Mod, soll sie die anderen nicht aufhalten
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
                    }
                    catch (Exception exception) when (exception is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
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
        public IReadOnlyList<string> ConnectedInstances => _sockets.Where(socket => socket.Value.State == WebSocketState.Open).Select(socket => socket.Key).ToList();

        public bool IsConnected(string instanceId) => _sockets.TryGetValue(instanceId, out var value) && value.State == WebSocketState.Open;

        private async Task acceptLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }
                _ = handle(client, cancellationToken);
            }
        }

        private async Task handle(TcpClient client, CancellationToken cancellationToken)
        {
            using var tcp = client;
            var stream = client.GetStream();
            try
            {
                // HTTP-Upgrade lesen (höchstens 8 KB)
                var request = await readHeaders(stream, cancellationToken);
                var key = header(request, "Sec-WebSocket-Key");
                var path = request.Split("\r\n")[0].Split(' ').ElementAtOrDefault(1) ?? "";
                var query = path.Contains('?') ? System.Web.HttpUtility.ParseQueryString(path[(path.IndexOf('?') + 1)..]) : null;
                var instance = query?["instance"];
                var token = query?["token"];
                if (key == null || instance == null || token == null || !_tokens.TryGetValue(instance, out var expected)
                    || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
                {
                    await stream.WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), cancellationToken);
                    return;
                }

                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                    $"Sec-WebSocket-Accept: {accept}\r\n\r\n"), cancellationToken);

                using var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, TimeSpan.FromSeconds(20));
                _sockets[instance] = socket;
                var buffer = new byte[64 * 1024];
                using var message = new MemoryStream();
                var tooLarge = false;
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    // Teilstücke zusammensetzen; zu große Nachrichten verwerfen
                    if (!tooLarge)
                    {
                        if (message.Length + result.Count > MaxMessageBytes)
                        {
                            tooLarge = true;
                        }
                        else
                        {
                            await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
                        }
                    }
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    var skip = tooLarge || result.MessageType != WebSocketMessageType.Text;
                    var length = (int)message.Length;
                    message.SetLength(0);
                    tooLarge = false;
                    if (skip)
                    {
                        continue;
                    }

                    try
                    {
                        using var document = JsonDocument.Parse(message.GetBuffer().AsMemory(0, length));
                        var type = document.RootElement.GetProperty("type").GetString();
                        if (type == "hello")
                        {
                            await Send(instance, "welcome", new { protocol = 2, features = Features });
                        }

                        if (type is { Length: > 0 and < 40 })
                        {
                            MessageReceived?.Invoke(instance, type, document.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default);
                        }
                    }
                    catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
                    {
                    }
                }
                _sockets.TryRemove(instance, out _);
            }
            catch (Exception exception) when (exception is IOException or WebSocketException or OperationCanceledException)
            {
            }
        }

        private async Task<string> readHeaders(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n"))
                {
                    break;
                }
            }
            return Encoding.ASCII.GetString(buffer, 0, total);
        }

        private string? header(string request, string name) =>
            request.Split("\r\n").Select(part => part.Split(':', 2)).FirstOrDefault(headerParts => headerParts.Length == 2 && headerParts[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                ?[1].Trim();

        public async ValueTask DisposeAsync()
        {
            if (_cts != null)
            {
                await _cts.CancelAsync();
            }

            _listener?.Stop();
            foreach (var socket in _sockets.Values)
            {
                socket.Abort();
            }
        }
    }
}
