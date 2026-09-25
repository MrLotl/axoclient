using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AxoClient.Instances;

public static partial class ServerPing
{
    public record Result(int Online, int Max, string Motd, long LatencyMs, string? Favicon, string? Version);

    public static async Task<Result> PingAsync(string address, int timeoutMs = 4000)
    {
        var (host, port) = ParseAddress(address);
        using var cts = new CancellationTokenSource(timeoutMs);
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
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cts.Token);

        using var json = JsonDocument.Parse(buffer);
        var root = json.RootElement;
        var players = root.TryGetProperty("players", out var p) ? p : default;
        return new Result(
            players.ValueKind == JsonValueKind.Object ? players.GetProperty("online").GetInt32() : 0,
            players.ValueKind == JsonValueKind.Object ? players.GetProperty("max").GetInt32() : 0,
            root.TryGetProperty("description", out var d) ? CleanMotd(ChatToText(d)) : "",
            latency,
            root.TryGetProperty("favicon", out var f) ? f.GetString() : null,
            root.TryGetProperty("version", out var v) && v.TryGetProperty("name", out var vn) ? vn.GetString() : null);
    }

    public static (string Host, int Port) ParseAddress(string address)
    {
        address = address.Trim();
        var colon = address.LastIndexOf(':');
        if (colon > 0 && int.TryParse(address[(colon + 1)..], out var port))
            return (address[..colon], port);
        return (address, 25565);
    }

    private static string ChatToText(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Array => string.Concat(e.EnumerateArray().Select(ChatToText)),
        JsonValueKind.Object =>
            (e.TryGetProperty("text", out var t) ? ChatToText(t) : "") +
            (e.TryGetProperty("extra", out var x) ? ChatToText(x) : ""),
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