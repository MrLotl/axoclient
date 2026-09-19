using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace McLauncher;

/// <summary>Ein Eintrag der Serverliste (servers.dat) plus Live-Status.</summary>
public class ServerEntry : INotifyPropertyChanged
{
    /// <summary>Original-NBT, damit unbekannte Felder (z.B. acceptTextures) beim Speichern erhalten bleiben.</summary>
    public NbtCompound Nbt { get; }

    public ServerEntry(NbtCompound nbt)
    {
        Nbt = nbt;
        if (nbt.Get<string>("icon") is { Length: > 0 } icon)
            _icon = DecodeIcon(icon);
    }

    public string Name
    {
        get => Nbt.Get<string>("name") ?? "";
        set { Nbt["name"] = value; Changed(); }
    }

    public string Address
    {
        get => Nbt.Get<string>("ip") ?? "";
        set { Nbt["ip"] = value; Changed(); }
    }

    private BitmapSource? _icon;
    public BitmapSource? Icon
    {
        get => _icon;
        set { _icon = value; Changed(); }
    }

    private string _status = "Status wird abgefragt...";
    public string Status
    {
        get => _status;
        set { _status = value; Changed(); }
    }

    private bool _online;
    public bool Online
    {
        get => _online;
        set { _online = value; Changed(); }
    }

    public static BitmapSource? DecodeIcon(string base64)
    {
        try
        {
            var comma = base64.IndexOf(',');
            var bytes = Convert.FromBase64String(comma >= 0 ? base64[(comma + 1)..] : base64);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Liest und schreibt die Serverliste (servers.dat) einer Instanz.</summary>
public class ServerStore(string gameDir)
{
    private string FilePath => Path.Combine(gameDir, "servers.dat");

    public List<ServerEntry> Load()
    {
        if (!File.Exists(FilePath))
            return [];
        try
        {
            var servers = Nbt.ReadFile(FilePath).Get<NbtList>("servers");
            return servers?.OfType<NbtCompound>().Select(c => new ServerEntry(c)).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<ServerEntry> servers)
    {
        Directory.CreateDirectory(gameDir);
        var list = new NbtList(10); // 10 = Compound
        list.AddRange(servers.Select(s => s.Nbt));
        Nbt.WriteFile(FilePath, new NbtCompound { ["servers"] = list });
    }

    public static ServerEntry Create(string name, string address) =>
        new(new NbtCompound { ["name"] = name, ["ip"] = address });

    /// <summary>Hängt Server an, deren Adresse noch nicht in der Liste steht. Gibt die Anzahl neuer Einträge zurück.</summary>
    public int Merge(IEnumerable<ServerEntry> incoming)
    {
        var servers = Load();
        var known = servers.Select(s => s.Address.Trim().ToLowerInvariant()).ToHashSet();
        var added = incoming.Where(s => known.Add(s.Address.Trim().ToLowerInvariant())).ToList();
        if (added.Count > 0)
            Save(servers.Concat(added));
        return added.Count;
    }
}

/// <summary>
/// Fragt den Status eines Servers ab (Server List Ping, wie die Serverliste im Spiel).
/// Funktioniert mit Servern ab Minecraft 1.7; SRV-DNS-Einträge werden nicht aufgelöst.
/// </summary>
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

        // Handshake (Paket 0x00): Protokollversion, Host, Port, nächster Zustand 1 = Status
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
        await SendPacketAsync(stream, [0x00], cts.Token); // Statusanfrage

        await ReadVarIntAsync(stream, cts.Token); // Paketlänge
        await ReadVarIntAsync(stream, cts.Token); // Paket-ID
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

    /// <summary>Wandelt eine Chat-Komponente ({"text":..,"extra":[..]}) in reinen Text um.</summary>
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
