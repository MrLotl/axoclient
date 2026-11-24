using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Common;
using AxoClient.DataModel.Social;
using AxoClient.Social.Contract;

namespace AxoClient.Social
{
    /// <summary>
    /// Echtzeit-Kanal zum Online-Service (<c>/v2/live</c>): solange auf einem Server gespielt wird, eine WebSocket-Verbindung
    /// in den Raum dieses Servers. Darüber laufen Emotes zwischen AxoClient-Spielern – ohne Mod oder Plugin auf dem Server –
    /// und die Infos einer offenen Welt (Version, Mods) für Freunde, die nachjoinen wollen.
    /// Bricht die Verbindung ab, wird mit wachsender Pause neu verbunden.
    /// </summary>
    public sealed partial class LiveChannel : IAsyncDisposable, ILiveChannel
    {
        private readonly IOnlineClient _client;
        private readonly IJsonHelper _jsonHelper;
        private readonly IAppLog _appLog;
        private readonly IAppInfo _appInfo;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly Lock _lock = new();
        private string? _server;
        private CancellationTokenSource? _cts;
        private ClientWebSocket? _socket;

        /// <summary>
        /// Ein anderer Spieler im selben Raum spielt ein Emote (UUID ohne Bindestriche, Emote-ID oder null = Abbruch). Bei eigenen
        /// Emotes (<c>c_…</c>) kommt die Animation (JSON-Text) und ihr Name darin mit.
        /// </summary>
        public event Action<RemoteEmote>? EmoteReceived;

        /// <summary>Gastgeber einer offenen Welt im selben Raum: Version, Loader und Mods (für "fehlende Mods").</summary>
        public event Action<string, JsonElement>? WorldReceived;

        /// <summary>Welt-Infos, die beim nächsten Verbinden gleich wieder gesendet werden (eigene offene Welt).</summary>
        private string? _worldMessage;

        /// <summary>Server, auf dem gerade gespielt wird (oder null): wechselt bei Bedarf den Raum.</summary>
        public void SetServer(string? address)
        {
            lock (_lock)
            {
                if (string.Equals(address, _server, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _server = address;
                _cts?.Cancel();
                _cts = null;
                if (address == null || !_client.CanUse)
                {
                    return;
                }

                _cts = new CancellationTokenSource();
                _ = run(address, _cts.Token);
            }
        }

        /// <summary>
        /// Eigenes Emote an alle anderen im Raum (null = abbrechen). Eigene Emotes (<c>c_…</c>) schicken ihre Animation mit, damit
        /// andere sie ohne Import sehen. Ohne Verbindung passiert nichts.
        /// </summary>
        public async Task SendEmote(string? emote, string? otherAnimation = null, string? animation = null)
        {
            if (emote != null && !emoteId().IsMatch(emote))
            {
                return;
            }

            var socket = _socket;
            if (socket is not { State: WebSocketState.Open })
            {
                return;
            }

            var message = emote != null && emote.StartsWith("c_", StringComparison.Ordinal)
                ? otherAnimation is { Length: > 0 and <= EmoteLibrary.MaxAnimationChars }
                    ? JsonSerializer.Serialize(new { type = "emote", emote, anim = otherAnimation, animation = animation ?? "default" }, _jsonHelper.Compact)
                    : null
                : JsonSerializer.Serialize(new { type = "emote", emote }, _jsonHelper.Compact);
            if (message != null)
            {
                await sendText(socket, message, CancellationToken.None);
            }
        }

        /// <summary>Eigene offene Welt bekannt machen (Version, Loader, Mods) oder mit <c>null</c> zurückziehen.</summary>
        public async Task SendWorld(object? worldInfo)
        {
            var message = JsonSerializer.Serialize(new { type = "world", info = worldInfo }, _jsonHelper.Compact);
            _worldMessage = worldInfo == null ? null : message;
            if (_socket is { State: WebSocketState.Open } socket)
            {
                await sendText(socket, message, CancellationToken.None);
            }
        }

        /// <summary>
        /// Schaut kurz in den Raum einer Adresse (ohne selbst etwas zu senden) und liefert die Welt-Infos des Gastgebers, falls
        /// dort jemand eine offene Welt bekannt gemacht hat.
        /// </summary>
        public async Task<JsonElement?> PeekWorld(string address, TimeSpan wait, CancellationToken cancellationToken)
        {
            if (!_client.CanUse)
            {
                return null;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(wait + TimeSpan.FromSeconds(4));
                using var socket = await connect(address, peek: true, timeout.Token);
                var deadline = DateTimeOffset.UtcNow + wait;
                while (socket.State == WebSocketState.Open && DateTimeOffset.UtcNow < deadline)
                {
                    using var once = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    once.CancelAfter(deadline - DateTimeOffset.UtcNow);
                    string? text;
                    try
                    {
                        text = await receiveText(socket, once.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    if (text == null)
                    {
                        break;
                    }

                    using var document = JsonDocument.Parse(text);
                    if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("type", out var type)
                                                                          && type.GetString() == "world"
                                                                          && document.RootElement.TryGetProperty("info", out var worldInfo)
                                                                          && worldInfo.ValueKind == JsonValueKind.Object)
                    {
                        return worldInfo.Clone();
                    }
                }
                return null;
            }
            catch (Exception exception) when (exception is WebSocketException or HttpRequestException or IOException or AxoException
                                           or OperationCanceledException or JsonException)
            {
                return null;
            }
        }

        private async Task run(string server, CancellationToken cancellationToken)
        {
            var delay = TimeSpan.FromSeconds(2);
            while (!cancellationToken.IsCancellationRequested)
            {
                var connectedAt = DateTimeOffset.UtcNow;
                try
                {
                    await connectAndReceive(server, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is WebSocketException or HttpRequestException or IOException or AxoException
                                               or OperationCanceledException)
                {
                    _appLog.Info($"Echtzeit-Kanal getrennt: {exception.Message}");
                }
                // Lief die Verbindung eine Weile, gleich wieder kurz warten; sonst immer länger (bis 1 Minute)
                delay = DateTimeOffset.UtcNow - connectedAt > TimeSpan.FromMinutes(1)
                    ? TimeSpan.FromSeconds(2)
                    : TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task<ClientWebSocket> connect(string server, bool peek, CancellationToken cancellationToken)
        {
            var token = await _client.Token(cancellationToken);
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            socket.Options.SetRequestHeader("X-AxoClient-Version", _appInfo.Version);
            socket.Options.CollectHttpResponseDetails = true;
            socket.Options.KeepAliveInterval = TimeSpan.Zero;
            var baseUrl = _client.BaseUrl.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
                .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);
            try
            {
                await socket.ConnectAsync(new Uri($"{baseUrl}/live?server={Uri.EscapeDataString(server)}{(peek ? "&peek=1" : "")}"), cancellationToken);
                return socket;
            }
            catch (WebSocketException) when (socket.HttpStatusCode == HttpStatusCode.Unauthorized)
            {
                // Token abgelaufen: beim nächsten Versuch neu anmelden
                _client.ForgetToken();
                socket.Dispose();
                throw;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        /// <summary>Eine ganze Textnachricht (auch über mehrere Rahmen, höchstens <see cref="MaxMessageBytes"/>); <c>null</c> = Verbindung zu.</summary>
        /// <summary>Platz für eine Emote-Animation samt JSON-Escaping (Anführungszeichen werden zu \").</summary>
        private const int MaxMessageBytes = 1024 * 1024;

        public LiveChannel(IOnlineClient client, IJsonHelper jsonHelper, IAppLog appLog, IAppInfo appInfo)
        {
            _client = client;
            _jsonHelper = jsonHelper;
            _appLog = appLog;
            _appInfo = appInfo;
        }

        private async Task<string?> receiveText(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using var message = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (message.Length + result.Count <= MaxMessageBytes)
                {
                    await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                if (result.MessageType != WebSocketMessageType.Text || message.Length >= MaxMessageBytes)
                {
                    message.SetLength(0);
                    continue;
                }
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }

        private async Task connectAndReceive(string server, CancellationToken cancellationToken)
        {
            using var socket = await connect(server, peek: false, cancellationToken);
            _socket = socket;
            if (_worldMessage is { } world)
            {
                await sendText(socket, world, cancellationToken);
            }

            using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = ping(socket, pingCts.Token);
            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var text = await receiveText(socket, cancellationToken);
                    if (text == null)
                    {
                        break;
                    }

                    handle(text);
                }
            }
            finally
            {
                if (_socket == socket)
                {
                    _socket = null;
                }

                await pingCts.CancelAsync();
                if (socket.State == WebSocketState.Open)
                {
                    try
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    }
                    catch (Exception exception) when (exception is WebSocketException or IOException)
                    {
                    }
                }
            }
        }

        /// <summary>"ping" hält die Verbindung offen; der Dienst antwortet mit "pong", ohne den Raum aufzuwecken.</summary>
        private async Task ping(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    await sendText(socket, "ping", cancellationToken);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or IOException)
            {
            }
        }

        private async Task sendText(ClientWebSocket socket, string text, CancellationToken cancellationToken)
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is WebSocketException or IOException or ObjectDisposedException)
            {
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void handle(string text)
        {
            if (text == "pong")
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                var type = root.GetProperty("type").GetString();
                var uuid = root.GetProperty("uuid").GetString();
                if (uuid is not { Length: 32 } || !uuid.All(Uri.IsHexDigit))
                {
                    return;
                }

                if (type == "world" && root.TryGetProperty("info", out var worldInfo) && worldInfo.ValueKind == JsonValueKind.Object)
                {
                    WorldReceived?.Invoke(uuid, worldInfo.Clone());
                    return;
                }
                if (type != "emote")
                {
                    return;
                }

                var emote = root.TryGetProperty("emote", out var emoteElement) && emoteElement.ValueKind == JsonValueKind.String ? emoteElement.GetString() : null;
                if (emote != null && !emoteId().IsMatch(emote))
                {
                    return;
                }

                string? otherAnimation = null, animation = null;
                if (emote != null && emote.StartsWith("c_", StringComparison.Ordinal))
                {
                    otherAnimation = root.TryGetProperty("anim", out var animationElement) && animationElement.ValueKind == JsonValueKind.String ? animationElement.GetString() : null;
                    animation = root.TryGetProperty("animation", out var rootValue) && rootValue.ValueKind == JsonValueKind.String ? rootValue.GetString() : null;
                    if (otherAnimation is not { Length: > 0 and <= EmoteLibrary.MaxAnimationChars })
                    {
                        return;
                    }
                    // Nur gültiges JSON weiterreichen; geprüft wird beim Empfänger
                    using var check = JsonDocument.Parse(otherAnimation);
                    if (check.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return;
                    }
                }
                EmoteReceived?.Invoke(new RemoteEmote(uuid, emote, otherAnimation, animation));
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
            }
        }

        public ValueTask DisposeAsync()
        {
            SetServer(null);
            return ValueTask.CompletedTask;
        }

        [GeneratedRegex("^[a-z0-9_]{1,32}$")]
        private partial Regex emoteId();
    }
}
