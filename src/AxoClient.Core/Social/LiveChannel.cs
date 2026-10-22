using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Core.Common;

namespace AxoClient.Core.Social;

/// <summary>Emote eines anderen Spielers (UUID ohne Bindestriche); bei eigenen Emotes mit Animation.</summary>
public sealed record RemoteEmote(string Uuid, string? Emote, string? Anim, string? Animation);

/// <summary>
/// Echtzeit-Kanal zum Online-Service (<c>/v2/live</c>): solange auf einem Server gespielt wird, eine WebSocket-Verbindung
/// in den Raum dieses Servers. Darüber laufen Emotes zwischen AxoClient-Spielern – ohne Mod oder Plugin auf dem Server –
/// und die Infos einer offenen Welt (Version, Mods) für Freunde, die nachjoinen wollen.
/// Bricht die Verbindung ab, wird mit wachsender Pause neu verbunden.
/// </summary>
public sealed partial class LiveChannel(OnlineClient client) : IAsyncDisposable
{
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
                return;
            _server = address;
            _cts?.Cancel();
            _cts = null;
            if (address == null || !client.CanUse)
                return;
            _cts = new CancellationTokenSource();
            _ = RunAsync(address, _cts.Token);
        }
    }

    /// <summary>
    /// Eigenes Emote an alle anderen im Raum (null = abbrechen). Eigene Emotes (<c>c_…</c>) schicken ihre Animation mit, damit
    /// andere sie ohne Import sehen. Ohne Verbindung passiert nichts.
    /// </summary>
    public async Task SendEmoteAsync(string? emote, string? anim = null, string? animation = null)
    {
        if (emote != null && !EmoteId().IsMatch(emote))
            return;
        var socket = _socket;
        if (socket is not { State: WebSocketState.Open })
            return;
        var message = emote != null && emote.StartsWith("c_", StringComparison.Ordinal)
            ? anim is { Length: > 0 and <= EmoteLibrary.MaxAnimationChars }
                ? JsonSerializer.Serialize(new { type = "emote", emote, anim, animation = animation ?? "default" }, Json.Compact)
                : null
            : JsonSerializer.Serialize(new { type = "emote", emote }, Json.Compact);
        if (message != null)
            await SendTextAsync(socket, message, CancellationToken.None);
    }

    /// <summary>Eigene offene Welt bekannt machen (Version, Loader, Mods) oder mit <c>null</c> zurückziehen.</summary>
    public async Task SendWorldAsync(object? info)
    {
        var message = JsonSerializer.Serialize(new { type = "world", info }, Json.Compact);
        _worldMessage = info == null ? null : message;
        if (_socket is { State: WebSocketState.Open } socket)
            await SendTextAsync(socket, message, CancellationToken.None);
    }

    /// <summary>
    /// Schaut kurz in den Raum einer Adresse (ohne selbst etwas zu senden) und liefert die Welt-Infos des Gastgebers, falls
    /// dort jemand eine offene Welt bekannt gemacht hat.
    /// </summary>
    public async Task<JsonElement?> PeekWorldAsync(string address, TimeSpan wait, CancellationToken ct)
    {
        if (!client.CanUse)
            return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(wait + TimeSpan.FromSeconds(4));
            using var socket = await ConnectAsync(address, peek: true, timeout.Token);
            var deadline = DateTimeOffset.UtcNow + wait;
            while (socket.State == WebSocketState.Open && DateTimeOffset.UtcNow < deadline)
            {
                using var once = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                once.CancelAfter(deadline - DateTimeOffset.UtcNow);
                string? text;
                try
                {
                    text = await ReceiveTextAsync(socket, once.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                if (text == null)
                    break;
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("type", out var t)
                                                                      && t.GetString() == "world"
                                                                      && doc.RootElement.TryGetProperty("info", out var info)
                                                                      && info.ValueKind == JsonValueKind.Object)
                    return info.Clone();
            }
            return null;
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException or AxoException
                                       or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    private async Task RunAsync(string server, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            var connectedAt = DateTimeOffset.UtcNow;
            try
            {
                await ConnectAndReceiveAsync(server, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException or AxoException
                                           or OperationCanceledException)
            {
                AppLog.Info($"Echtzeit-Kanal getrennt: {ex.Message}");
            }
            // Lief die Verbindung eine Weile, gleich wieder kurz warten; sonst immer länger (bis 1 Minute)
            delay = DateTimeOffset.UtcNow - connectedAt > TimeSpan.FromMinutes(1)
                ? TimeSpan.FromSeconds(2)
                : TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<ClientWebSocket> ConnectAsync(string server, bool peek, CancellationToken ct)
    {
        var token = await client.TokenAsync(ct);
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        socket.Options.SetRequestHeader("X-AxoClient-Version", AppInfo.Version);
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        var baseUrl = client.BaseUrl.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);
        try
        {
            await socket.ConnectAsync(new Uri($"{baseUrl}/live?server={Uri.EscapeDataString(server)}{(peek ? "&peek=1" : "")}"), ct);
            return socket;
        }
        catch (WebSocketException) when (socket.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            // Token abgelaufen: beim nächsten Versuch neu anmelden
            client.ForgetToken();
            socket.Dispose();
            throw;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Eine ganze Textnachricht (auch über mehrere Rahmen, höchstens 64 KB); <c>null</c> = Verbindung zu.</summary>
    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (message.Length + result.Count <= 64 * 1024)
                await message.WriteAsync(buffer.AsMemory(0, result.Count), ct);
            if (!result.EndOfMessage)
                continue;
            if (result.MessageType != WebSocketMessageType.Text || message.Length >= 64 * 1024)
            {
                message.SetLength(0);
                continue;
            }
            return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
        }
    }

    private async Task ConnectAndReceiveAsync(string server, CancellationToken ct)
    {
        using var socket = await ConnectAsync(server, peek: false, ct);
        _socket = socket;
        if (_worldMessage is { } world)
            await SendTextAsync(socket, world, ct);
        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = PingAsync(socket, pingCts.Token);
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var text = await ReceiveTextAsync(socket, ct);
                if (text == null)
                    break;
                Handle(text);
            }
        }
        finally
        {
            if (_socket == socket)
                _socket = null;
            await pingCts.CancelAsync();
            if (socket.State == WebSocketState.Open)
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch (Exception ex) when (ex is WebSocketException or IOException)
                {
                }
        }
    }

    /// <summary>"ping" hält die Verbindung offen; der Dienst antwortet mit "pong", ohne den Raum aufzuwecken.</summary>
    private async Task PingAsync(ClientWebSocket socket, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                await SendTextAsync(socket, "ping", ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException)
        {
        }
    }

    private async Task SendTextAsync(ClientWebSocket socket, string text, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void Handle(string text)
    {
        if (text == "pong")
            return;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;
            var type = root.GetProperty("type").GetString();
            var uuid = root.GetProperty("uuid").GetString();
            if (uuid is not { Length: 32 } || !uuid.All(Uri.IsHexDigit))
                return;
            if (type == "world" && root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
            {
                WorldReceived?.Invoke(uuid, info.Clone());
                return;
            }
            if (type != "emote")
                return;
            var emote = root.TryGetProperty("emote", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            if (emote != null && !EmoteId().IsMatch(emote))
                return;
            string? anim = null, animation = null;
            if (emote != null && emote.StartsWith("c_", StringComparison.Ordinal))
            {
                anim = root.TryGetProperty("anim", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                animation = root.TryGetProperty("animation", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                if (anim is not { Length: > 0 and <= EmoteLibrary.MaxAnimationChars })
                    return;
                // Nur gültiges JSON weiterreichen; geprüft wird beim Empfänger
                using var check = JsonDocument.Parse(anim);
                if (check.RootElement.ValueKind != JsonValueKind.Object)
                    return;
            }
            EmoteReceived?.Invoke(new RemoteEmote(uuid, emote, anim, animation));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        SetServer(null);
        return ValueTask.CompletedTask;
    }

    [GeneratedRegex("^[a-z0-9_]{1,32}$")]
    private static partial Regex EmoteId();
}
