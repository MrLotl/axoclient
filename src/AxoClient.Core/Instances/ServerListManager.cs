using AxoClient.Core.Common;

namespace AxoClient.Core.Instances;

public sealed record ServerEntry(int Index, string Name, string Address, string? IconUrl, bool AcceptTextures);

/// <summary>Die Serverliste des Spiels (<c>servers.dat</c>), direkt gelesen und geschrieben.</summary>
public sealed class ServerListManager(AppPaths paths)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string File(string id) => Path.Combine(paths.GameDir(Text.RequireId(id)), "servers.dat");

    public IReadOnlyList<ServerEntry> List(string instanceId)
    {
        var list = ReadList(instanceId);
        return list.Select((c, i) => ToEntry(instanceId, i, (NbtCompound)c)).ToList();
    }

    /// <summary>Normalisierte Adressen aller Server einer Instanz ("Freund spielt auf deinem Server").</summary>
    public IReadOnlySet<string> NormalizedAddresses(string instanceId) =>
        List(instanceId).Select(s => ServerPing.Normalize(s.Address)).ToHashSet();

    private static ServerEntry ToEntry(string instanceId, int index, NbtCompound c)
    {
        var icon = c.Get<string>("icon");
        return new ServerEntry(index, Text.Clean(c.Get<string>("name"), 80), Text.Clean(c.Get<string>("ip"), 255),
            icon is { Length: > 0 } ? $"axo://server-icon/{instanceId}/{index}" : null,
            c.TryGetValue("acceptTextures", out var t) && t is sbyte b && b != 0);
    }

    /// <summary>Servericon als PNG (Base64 im NBT).</summary>
    public byte[]? Icon(string instanceId, int index)
    {
        var list = ReadList(instanceId);
        if (index < 0 || index >= list.Count || ((NbtCompound)list[index]).Get<string>("icon") is not { Length: > 0 } icon)
            return null;
        try
        {
            return Convert.FromBase64String(icon);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public async Task AddAsync(string instanceId, string name, string address, int? atIndex = null)
    {
        Validate(name, address);
        await ChangeAsync(instanceId, list =>
        {
            var entry = new NbtCompound { ["name"] = Text.Clean(name, 80), ["ip"] = address.Trim() };
            if (atIndex is { } i && i >= 0 && i <= list.Count)
                list.Insert(i, entry);
            else
                list.Add(entry);
        });
    }

    public async Task EditAsync(string instanceId, int index, string name, string address)
    {
        Validate(name, address);
        await ChangeAsync(instanceId, list =>
        {
            var entry = Get(list, index);
            if (entry.Get<string>("ip") != address.Trim())
                entry.Remove("icon");
            entry["name"] = Text.Clean(name, 80);
            entry["ip"] = address.Trim();
        });
    }

    public Task RemoveAsync(string instanceId, int index) => ChangeAsync(instanceId, list =>
    {
        Get(list, index);
        list.RemoveAt(index);
    });

    public Task MoveAsync(string instanceId, int from, int to) => ChangeAsync(instanceId, list =>
    {
        var entry = Get(list, from);
        list.RemoveAt(from);
        list.Insert(Math.Clamp(to, 0, list.Count), entry);
    });

    private static NbtCompound Get(NbtList list, int index) =>
        index >= 0 && index < list.Count ? (NbtCompound)list[index] : throw new AxoException("errors.notFound");

    private static void Validate(string name, string address)
    {
        if (Text.Clean(name, 80).Length == 0)
            throw new AxoException("servers.nameMissing");
        if (!ServerPing.IsValidAddress(address))
            throw new AxoException("servers.addressInvalid");
    }

    private NbtList ReadList(string instanceId)
    {
        var file = File(instanceId);
        if (!System.IO.File.Exists(file))
            return new NbtList(10);
        try
        {
            return Nbt.ReadFile(file).Get<NbtList>("servers") ?? new NbtList(10);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            AppLog.Warn("servers.dat nicht lesbar", ex);
            return new NbtList(10);
        }
    }

    private async Task ChangeAsync(string instanceId, Action<NbtList> change)
    {
        await _lock.WaitAsync();
        try
        {
            var file = File(instanceId);
            NbtCompound root;
            try
            {
                root = System.IO.File.Exists(file) ? Nbt.ReadFile(file) : new NbtCompound();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
            {
                root = new NbtCompound();
            }
            var list = root.Get<NbtList>("servers") ?? new NbtList(10);
            change(list);
            root["servers"] = list;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Nbt.WriteFile(file, root);
        }
        finally
        {
            _lock.Release();
        }
    }
}
