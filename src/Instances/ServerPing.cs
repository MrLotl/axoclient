using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Common;
using AxoClient.DataModel.Instances;
using AxoClient.Instances.Contract;

namespace AxoClient.Instances
{
    /// <summary>Server List Ping (Minecraft-Protokoll), wie die Serverliste im Spiel.</summary>
    public partial class ServerPing : IServerPing
    {
        private readonly ITextHelper _textHelper;
        public ServerPing(ITextHelper textHelper)
        {
            _textHelper = textHelper;
        }

        public async Task<ServerStatus> Ping(string address, int timeoutMs = 4000, CancellationToken cancellationToken = default)
        {
            try
            {
                return await pingCore(address, timeoutMs, cancellationToken);
            }
            catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException or InvalidDataException
                                           or JsonException or ArgumentException && !cancellationToken.IsCancellationRequested)
            {
                return new ServerStatus(false, 0, 0, "", null, null, null);
            }
        }

        private async Task<ServerStatus> pingCore(string address, int timeoutMs, CancellationToken outer)
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
            writeVarInt(handshake, 0x00);
            writeVarInt(handshake, -1);
            var hostBytes = Encoding.UTF8.GetBytes(host);
            writeVarInt(handshake, hostBytes.Length);
            handshake.AddRange(hostBytes);
            handshake.Add((byte)(port >> 8));
            handshake.Add((byte)port);
            writeVarInt(handshake, 1);
            await sendPacket(stream, handshake, cts.Token);
            await sendPacket(stream, [0x00], cts.Token);

            await readVarInt(stream, cts.Token);
            await readVarInt(stream, cts.Token);
            var length = await readVarInt(stream, cts.Token);
            if (length is <= 0 or > 1024 * 1024)
            {
                throw new InvalidDataException("Ungültige Serverantwort.");
            }

            var buffer = new byte[length];
            await stream.ReadExactlyAsync(buffer, cts.Token);

            using var json = JsonDocument.Parse(buffer);
            var root = json.RootElement;
            var players = root.TryGetProperty("players", out var playersElement) && playersElement.ValueKind == JsonValueKind.Object ? playersElement : default;
            var favicon = root.TryGetProperty("favicon", out var faviconElement) ? faviconElement.GetString() : null;
            return new ServerStatus(
                true,
                players.ValueKind == JsonValueKind.Object && players.TryGetProperty("online", out var on) ? on.GetInt32() : 0,
                players.ValueKind == JsonValueKind.Object && players.TryGetProperty("max", out var max) ? max.GetInt32() : 0,
                root.TryGetProperty("description", out var description) ? _textHelper.Clean(cleanMotd(chatToText(description, 0)), 200) : "",
                latency,
                favicon is { Length: < 64 * 1024 } && favicon.StartsWith("data:image/png;base64,") ? favicon : null,
                root.TryGetProperty("version", out var version) && version.TryGetProperty("name", out var name) ? _textHelper.Clean(name.GetString(), 60) : null);
        }

        /// <summary>"Play.Example.net" und "play.example.net:25565" gelten als gleich.</summary>
        public string Normalize(string address)
        {
            var (host, port) = ParseAddress(address);
            return $"{host.ToLowerInvariant().TrimEnd('.')}:{port}";
        }

        public (string Host, int Port) ParseAddress(string address)
        {
            address = address.Trim();
            if (address.StartsWith('[') && address.Contains(']'))
            {
                var end = address.IndexOf(']');
                var hostV6 = address[1..end];
                return (hostV6, address.Length > end + 2 && int.TryParse(address[(end + 2)..], out var parsed) ? parsed : 25565);
            }
            var colon = address.LastIndexOf(':');
            if (colon > 0 && address.IndexOf(':') == colon && int.TryParse(address[(colon + 1)..], out var port)
                && port is > 0 and < 65536)
            {
                return (address[..colon], port);
            }

            return (address, 25565);
        }

        public bool IsValidAddress(string? address) =>
            !string.IsNullOrWhiteSpace(address) && address.Length <= 255 && !address.Any(char.IsWhiteSpace)
            && !address.Contains('/') && !address.Contains('\\');

        private string chatToText(JsonElement component, int depth) => depth > 20
            ? ""
            : component.ValueKind switch
            {
                JsonValueKind.String => component.GetString() ?? "",
                JsonValueKind.Array => string.Concat(component.EnumerateArray().Select(part => chatToText(part, depth + 1))),
                JsonValueKind.Object =>
                    (component.TryGetProperty("text", out var text) ? chatToText(text, depth + 1) : "") +
                    (component.TryGetProperty("extra", out var extra) ? chatToText(extra, depth + 1) : ""),
                _ => ""
            };

        [GeneratedRegex("§.")]
        private partial Regex formattingCodes();

        private string cleanMotd(string motd) =>
            string.Join(" ", formattingCodes().Replace(motd, "").Split('\n', StringSplitOptions.TrimEntries)).Trim();

        private async Task sendPacket(NetworkStream stream, List<byte> payload, CancellationToken cancellationToken)
        {
            var packet = new List<byte>();
            writeVarInt(packet, payload.Count);
            packet.AddRange(payload);
            await stream.WriteAsync(packet.ToArray(), cancellationToken);
        }

        private void writeVarInt(List<byte> buffer, int value)
        {
            var remaining = (uint)value;
            do
            {
                var nextByte = (byte)(remaining & 0x7F);
                remaining >>= 7;
                buffer.Add(remaining != 0 ? (byte)(nextByte | 0x80) : nextByte);
            } while (remaining != 0);
        }

        private async Task<int> readVarInt(NetworkStream stream, CancellationToken cancellationToken)
        {
            int value = 0, shift = 0;
            var one = new byte[1];
            while (true)
            {
                await stream.ReadExactlyAsync(one, cancellationToken);
                value |= (one[0] & 0x7F) << shift;
                if ((one[0] & 0x80) == 0)
                {
                    return value;
                }

                shift += 7;
                if (shift > 35)
                {
                    throw new InvalidDataException("Ungültige Serverantwort.");
                }
            }
        }
    }
}
