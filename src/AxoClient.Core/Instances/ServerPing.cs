using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Core.Common;

namespace AxoClient.Core.Instances;

public sealed record ServerStatus(bool Online, int Players, int MaxPlayers, string Motd, long? LatencyMs, string? Favicon, string? Version);

/// <summary>Server List Ping (Minecraft-Protokoll), wie die Serverliste im Spiel.</summary>
public static partial class ServerPing
{
    public static async Task<ServerStatus> PingAsync(string address, int timeoutMs = 4000, CancellationToken ct = default)
    {
        try
        {
            return await PingCoreAsync(address, timeoutMs, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or InvalidDataException
                                       or JsonException or ArgumentException && !ct.IsCancellationRequested)
        {
            return new ServerStatus(false, 0, 0, "", null, null, null);
        }
    }

    private static async Task<ServerStatus> PingCoreAsync(string address, int timeoutMs, CancellationToken outer)
    {
        var (host, port) = ParseAddress(address);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(timeoutMs);
        using var client = new TcpClient();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await client.ConnectAsync(host, port, cts.Token);
        var latency = watch.ElapsedMilliseconds;
        await using var stream = client.GetStream();

        var handshake = new List<byte>();
        WriteVarInt(handshake, 0x00);
        WriteVarInt(handshake, -1);
        var hostBytes = Encoding.UTF8.GetBytes(host);
        WriteVarInt(handshake, hostBytes.Length);
        handshake.AddRange(hostBytes);
        handshake.Add((byte)(port >> 8));
        handshake.Add((byte)port);
        WriteVarInt(handshake, 1);
        await SendPacketAsync(stream, handshake, cts.Token);
        await SendPacketAsync(stream, [0x00], cts.Token);

        await ReadVarIntAsync(stream, cts.Token);
        await ReadVarIntAsync(stream, cts.Token);
        var length = await ReadVarIntAsync(stream, cts.Token);
        if (length is <= 0 or > 1024 * 1024)
            throw new InvalidDataException("Ungültige Serverantwort.");
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cts.Token);

        using var json = JsonDocument.Parse(buffer);
        var root = json.RootElement;
        var players = root.TryGetProperty("players", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        var favicon = root.TryGetProperty("favicon", out var f) ? f.GetString() : null;
        return new ServerStatus(
            true,
            players.ValueKind == JsonValueKind.Object && players.TryGetProperty("online", out var on) ? on.GetInt32() : 0,
            players.ValueKind == JsonValueKind.Object && players.TryGetProperty("max", out var max) ? max.GetInt32() : 0,
            root.TryGetProperty("description", out var d) ? Text.Clean(CleanMotd(ChatToText(d, 0)), 200) : "",
            latency,
            favicon is { Length: < 64 * 1024 } && favicon.StartsWith("data:image/png;base64,") ? favicon : null,
            root.TryGetProperty("version", out var v) && v.TryGetProperty("name", out var vn) ? Text.Clean(vn.GetString(), 60) : null);
    }

    /// <summary>"Play.Example.net" und "play.example.net:25565" gelten als gleich.</summary>
    public static string Normalize(string address)
    {
        var (host, port) = ParseAddress(address);
        return $"{host.ToLowerInvariant().TrimEnd('.')}:{port}";
    }

    public static (string Host, int Port) ParseAddress(string address)
    {
        address = address.Trim();
        if (address.StartsWith('[') && address.Contains(']'))
        {
            var end = address.IndexOf(']');
            var hostV6 = address[1..end];
            return (hostV6, address.Length > end + 2 && int.TryParse(address[(end + 2)..], out var p6) ? p6 : 25565);
        }
        var colon = address.LastIndexOf(':');
        if (colon > 0 && address.IndexOf(':') == colon && int.TryParse(address[(colon + 1)..], out var port)
            && port is > 0 and < 65536)
            return (address[..colon], port);
        return (address, 25565);
    }

    public static bool IsValidAddress(string? address) =>
        !string.IsNullOrWhiteSpace(address) && address.Length <= 255 && !address.Any(char.IsWhiteSpace)
        && !address.Contains('/') && !address.Contains('\\');

    private static string ChatToText(JsonElement e, int depth) => depth > 20
        ? ""
        : e.ValueKind switch
        {
            JsonValueKind.String => e.GetString() ?? "",
            JsonValueKind.Array => string.Concat(e.EnumerateArray().Select(x => ChatToText(x, depth + 1))),
            JsonValueKind.Object =>
                (e.TryGetProperty("text", out var t) ? ChatToText(t, depth + 1) : "") +
                (e.TryGetProperty("extra", out var x) ? ChatToText(x, depth + 1) : ""),
            _ => ""
        };

    [GeneratedRegex("§.")]
    private static partial Regex FormattingCodes();

    private static string CleanMotd(string motd) =>
        string.Join(" ", FormattingCodes().Replace(motd, "").Split('\n', StringSplitOptions.TrimEntries)).Trim();

    private static async Task SendPacketAsync(NetworkStream stream, List<byte> payload, CancellationToken ct)
    {
        var packet = new List<byte>();
        WriteVarInt(packet, payload.Count);
        packet.AddRange(payload);
        await stream.WriteAsync(packet.ToArray(), ct);
    }

    private static void WriteVarInt(List<byte> buffer, int value)
    {
        var v = (uint)value;
        do
        {
            var b = (byte)(v & 0x7F);
            v >>= 7;
            buffer.Add(v != 0 ? (byte)(b | 0x80) : b);
        } while (v != 0);
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken ct)
    {
        int value = 0, shift = 0;
        var one = new byte[1];
        while (true)
        {
            await stream.ReadExactlyAsync(one, ct);
            value |= (one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0)
                return value;
            shift += 7;
            if (shift > 35)
                throw new InvalidDataException("Ungültige Serverantwort.");
        }
    }
}
