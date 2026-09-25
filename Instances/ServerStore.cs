using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AxoClient.Instances;

public class ServerEntry : INotifyPropertyChanged
{
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

    public async Task RefreshStatusAsync(Func<bool> stillWanted, bool withMotd)
    {
        Status = "Status wird abgefragt...";
        Online = false;
        try
        {
            var result = await ServerPing.PingAsync(Address);
            if (!stillWanted())
                return;
            Online = true;
            Status = $"{result.Online}/{result.Max} Spieler · {result.LatencyMs} ms" +
                     (withMotd && result.Motd.Length > 0 ? $" · {result.Motd}" : "");
            if (result.Favicon != null)
                Icon = DecodeIcon(result.Favicon) ?? Icon;
        }
        catch (Exception ex)
        {
            if (stillWanted())
                Status = "Nicht erreichbar: " + ErrorReport.Short(ex);
        }
    }

    private static BitmapSource? DecodeIcon(string base64)
    {
        try
        {
            var comma = base64.IndexOf(',');
            return Images.FromBytes(Convert.FromBase64String(comma >= 0 ? base64[(comma + 1)..] : base64));
        }
        catch (FormatException ex)
        {
            ErrorReport.Log("Serversymbol dekodieren", ex);
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ServerStore(string gameDir)
{
    private const byte CompoundTag = 10;

    private string FilePath => Path.Combine(gameDir, "servers.dat");

    public Exception? LoadError { get; private set; }

    public List<ServerEntry> Load()
    {
        if (!File.Exists(FilePath))
            return [];
        try
        {
            var servers = Nbt.ReadFile(FilePath).Get<NbtList>("servers");
            LoadError = null;
            return servers?.OfType<NbtCompound>().Select(c => new ServerEntry(c)).ToList() ?? [];
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"Serverliste \"{FilePath}\" lesen", ex);
            LoadError = ex;
            return [];
        }
    }

    public void Save(IEnumerable<ServerEntry> servers)
    {
        Directory.CreateDirectory(gameDir);
        var list = new NbtList(CompoundTag);
        list.AddRange(servers.Select(s => s.Nbt));
        Nbt.WriteFile(FilePath, new NbtCompound { ["servers"] = list });
    }

    public static ServerEntry Create(string name, string address) =>
        new(new NbtCompound { ["name"] = name, ["ip"] = address });

    public int Merge(IEnumerable<ServerEntry> incoming)
    {
        var servers = Load();
        var known = servers.Select(s => s.Address.Trim().ToLowerInvariant()).ToHashSet();
        var added = incoming.Where(s => known.Add(s.Address.Trim().ToLowerInvariant())).ToList();
        if (added.Count > 0)
            Save(servers.Concat(added));
        return added.Count;
    }

    public List<(string Name, string Address)> Addresses() => Load()
        .Select(s => (s.Name, Address: s.Address.Trim()))
        .Where(s => s.Address.Length > 0)
        .DistinctBy(s => s.Address, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
