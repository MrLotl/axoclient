using System.Collections.Concurrent;
using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;

namespace AxoClient.Core.Instances;

/// <summary>Anlegen, Ändern, Kopieren und Löschen von Instanzen. Jede Instanz ist ein eigener Ordner.</summary>
public sealed class InstanceManager(AppPaths paths, EventHub events, ProcessRunner processes)
{
    public const int MaxImageBytes = 5 * 1024 * 1024;
    private readonly ConcurrentDictionary<string, Instance> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonStore<Instance> StoreOf(string id) => new(paths.Fs, paths.InstanceJson(Text.RequireId(id)));
    public JsonStore<ContentDocument> ContentOf(string id) => new(paths.Fs, paths.ContentJson(Text.RequireId(id)));

    public string GameDir(string id) => paths.GameDir(Text.RequireId(id));

    public static string ProcessKey(string id) => "instance:" + id;

    public IReadOnlyList<string> Ids() =>
        Directory.Exists(paths.Instances)
            ? Directory.EnumerateDirectories(paths.Instances).Select(Path.GetFileName).OfType<string>()
                .Where(id => Text.IsValidId(id) && File.Exists(paths.InstanceJson(id))).ToList()
            : [];

    public async Task<IReadOnlyList<Instance>> LoadAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(paths.Instances))
            return [];
        var result = new List<Instance>();
        foreach (var dir in Directory.EnumerateDirectories(paths.Instances))
        {
            var id = Path.GetFileName(dir);
            if (!Text.IsValidId(id) || !File.Exists(paths.InstanceJson(id)))
                continue;
            try
            {
                var instance = await StoreOf(id).LoadAsync(ct);
                instance.Id = id;
                _cache[id] = instance;
                result.Add(instance);
            }
            catch (Exception ex) when (ex is IOException or AxoException)
            {
                AppLog.Warn($"Instanz {id} konnte nicht gelesen werden", ex);
            }
        }
        return result;
    }

    public async Task<Instance> GetAsync(string id, CancellationToken ct = default)
    {
        Text.RequireId(id);
        if (_cache.TryGetValue(id, out var cached))
            return cached;
        if (!File.Exists(paths.InstanceJson(id)))
            throw new AxoException("instances.notFound");
        var instance = await StoreOf(id).LoadAsync(ct);
        instance.Id = id;
        _cache[id] = instance;
        return instance;
    }

    public async Task<IReadOnlyList<InstanceSummary>> ListAsync(CancellationToken ct = default)
    {
        var all = await LoadAllAsync(ct);
        var summaries = new List<InstanceSummary>();
        foreach (var instance in all)
            summaries.Add(await SummaryAsync(instance, ct));
        return summaries.OrderByDescending(s => s.LastPlayed ?? s.CreatedAt).ToList();
    }

    public async Task<InstanceSummary> SummaryAsync(Instance instance, CancellationToken ct = default)
    {
        var playtime = await new JsonStore<ActivityLog>(paths.Fs, paths.PlaytimeJson(instance.Id)).LoadAsync(ct);
        var modCount = 0;
        if (File.Exists(paths.ContentJson(instance.Id)))
            modCount = (await ContentOf(instance.Id).LoadAsync(ct)).Mods.Count(m => m.Source != ContentSource.AxoClient);
        var hasIcon = instance.Images.Icon != null && File.Exists(Path.Combine(paths.Instance(instance.Id), instance.Images.Icon));
        var hasBanner = instance.Images.Banner != null && File.Exists(Path.Combine(paths.Instance(instance.Id), instance.Images.Banner));
        var version = ImageVersion(instance.Id);
        return new InstanceSummary(instance.Id, instance.Name, instance.Minecraft.Version, instance.Loader.Type,
            instance.Loader.Version, $"/axo/instance-icon/{instance.Id}?v={version}",
            hasBanner && !instance.Images.BannerAuto ? $"/axo/instance-banner/{instance.Id}?v={version}" : null,
            hasIcon, playtime.LastPlayed, playtime.TotalSeconds, processes.Get(ProcessKey(instance.Id)) != null, modCount,
            instance.CreatedAt, instance.ModLock.Level);
    }

    private long ImageVersion(string id)
    {
        var icon = Path.Combine(paths.Instance(id), "icon.png");
        var banner = Path.Combine(paths.Instance(id), "banner.png");
        return Math.Max(File.Exists(icon) ? File.GetLastWriteTimeUtc(icon).Ticks : 0,
            File.Exists(banner) ? File.GetLastWriteTimeUtc(banner).Ticks : 0) % 1_000_000_007;
    }

    public async Task<Instance> CreateAsync(CreateInstanceRequest request, OriginType origin = OriginType.Empty,
        CancellationToken ct = default)
    {
        var name = InstanceRules.CleanName(request.Name);
        if (!LoaderManager.IsSafeVersion(request.MinecraftVersion))
            throw new AxoException("instances.create.versionMissing");
        if (request.LoaderVersion != null && !LoaderManager.IsSafeVersion(request.LoaderVersion))
            throw new AxoException("errors.invalidInput");

        await _lock.WaitAsync(ct);
        try
        {
            var id = Text.NewId(name);
            while (Directory.Exists(paths.Instance(id)))
                id = Text.NewId(name);

            var instance = new Instance
            {
                Id = id,
                Name = name,
                Minecraft = new MinecraftVersionRef(request.MinecraftVersion, VersionType.Release),
                Loader = new LoaderRef(request.Loader, request.Loader == LoaderType.Vanilla ? null : request.LoaderVersion),
                MemoryMb = request.MemoryMb is { } mb ? Math.Clamp(mb, 512, 65536) : null,
                Origin = new InstanceOrigin { Type = origin },
                CreatedAt = DateTimeOffset.UtcNow
            };

            var dir = paths.Instance(id);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(paths.GameDir(id));
            foreach (var sub in new[] { "mods", "resourcepacks", "shaderpacks", "saves", "screenshots", "config" })
                Directory.CreateDirectory(Path.Combine(paths.GameDir(id), sub));
            Directory.CreateDirectory(paths.InstanceBackups(id));

            await StoreOf(id).SaveAsync(instance, ct);
            await ContentOf(id).SaveAsync(new ContentDocument(), ct);
            _cache[id] = instance;
            Publish();
            return instance;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Kopiert eine Instanz samt Welten und Mods. Spielzeit, Logs und Crash-Reports nicht.</summary>
    public async Task<Instance> CopyAsync(string sourceId, string newName, JobContext job)
    {
        var source = await GetAsync(sourceId, job.Token);
        var copy = await CreateAsync(new CreateInstanceRequest(newName, source.Minecraft.Version, source.Loader.Type,
            source.Loader.Version, source.MemoryMb), OriginType.Copy, job.Token);
        copy.JavaArgs = source.JavaArgs;
        copy.Minecraft = source.Minecraft;
        copy.AxoMod = source.AxoMod;
        copy.ModLock = new ModLock { Level = source.ModLock.Level, PackMods = [.. source.ModLock.PackMods] };
        copy.Origin.CopiedFrom = sourceId;

        var from = paths.GameDir(sourceId);
        var to = paths.GameDir(copy.Id);
        var files = Directory.Exists(from)
            ? Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)
                .Where(f => !IsTransient(Path.GetRelativePath(from, f))).ToList()
            : [];
        for (var i = 0; i < files.Count; i++)
        {
            job.Token.ThrowIfCancellationRequested();
            var target = Path.Combine(to, Path.GetRelativePath(from, files[i]));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(files[i], target, overwrite: true);
            job.Report((double)(i + 1) / files.Count);
        }
        foreach (var image in new[] { "icon.png", "banner.png" })
        {
            var file = Path.Combine(paths.Instance(sourceId), image);
            if (File.Exists(file))
                File.Copy(file, Path.Combine(paths.Instance(copy.Id), image), overwrite: true);
        }
        copy.Images = new InstanceImages
        {
            Icon = source.Images.Icon, Banner = source.Images.Banner, BannerAuto = source.Images.BannerAuto
        };
        if (File.Exists(paths.ContentJson(sourceId)))
            File.Copy(paths.ContentJson(sourceId), paths.ContentJson(copy.Id), overwrite: true);
        await SaveAsync(copy, job.Token);
        return copy;
    }

    internal static bool IsTransient(string relative)
    {
        var first = relative.Replace('\\', '/').Split('/')[0];
        return first is "logs" or "crash-reports" or ".fabric" or "debug" || relative.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
               || Path.GetFileName(relative).StartsWith("hs_err_pid");
    }

    public async Task<Instance> UpdateAsync(string id, InstanceUpdate update, CancellationToken ct = default)
    {
        var instance = await GetAsync(id, ct);
        if (update.Name != null)
            instance.Name = InstanceRules.CleanName(update.Name);
        if (update.MinecraftVersion != null)
        {
            if (!LoaderManager.IsSafeVersion(update.MinecraftVersion))
                throw new AxoException("errors.invalidInput");
            if (update.MinecraftVersion != instance.Minecraft.Version)
                instance.Minecraft = instance.Minecraft with { Version = update.MinecraftVersion };
        }
        if (update.Loader is { } loader)
        {
            if (update.LoaderVersion != null && !LoaderManager.IsSafeVersion(update.LoaderVersion))
                throw new AxoException("errors.invalidInput");
            instance.Loader = new LoaderRef(loader, loader == LoaderType.Vanilla ? null : update.LoaderVersion);
        }
        if (update.ResetMemory)
            instance.MemoryMb = null;
        else if (update.MemoryMb is { } mb)
            instance.MemoryMb = Math.Clamp(mb, 512, 65536);
        if (update.JavaArgs != null)
            instance.JavaArgs = Text.Clean(update.JavaArgs, 2000);
        if (update.BannerAuto is { } auto)
            instance.Images.BannerAuto = auto;
        if (update.AxoMod is { } axo)
            instance.AxoMod = axo;
        await SaveAsync(instance, ct);
        return instance;
    }

    public async Task SaveAsync(Instance instance, CancellationToken ct = default)
    {
        await StoreOf(instance.Id).SaveAsync(instance, ct);
        _cache[instance.Id] = instance;
        Publish();
    }

    /// <summary>Löschen landet im Papierkorb, damit nichts unwiderruflich weg ist.</summary>
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        Text.RequireId(id);
        if (processes.Get(ProcessKey(id)) != null)
            throw new AxoException("instances.running");
        var dir = paths.Instance(id);
        if (!Directory.Exists(dir))
            return;
        await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin), ct);
        _cache.TryRemove(id, out _);
        Publish();
    }

    /// <summary>Speichert Instanz- oder Bannerbild. Nur PNG und JPEG, höchstens 5 MB.</summary>
    public async Task SetImageAsync(string id, bool banner, byte[]? data, CancellationToken ct = default)
    {
        var instance = await GetAsync(id, ct);
        var file = banner ? "banner.png" : "icon.png";
        var path = Path.Combine(paths.Instance(id), file);
        if (data == null)
        {
            if (File.Exists(path))
                File.Delete(path);
            if (banner)
            {
                instance.Images.Banner = null;
                instance.Images.BannerAuto = true;
            }
            else
                instance.Images.Icon = null;
        }
        else
        {
            if (data.Length > MaxImageBytes || !ImageFormats.IsPngOrJpeg(data))
                throw new AxoException("errors.invalidImage");
            await File.WriteAllBytesAsync(path, data, ct);
            if (banner)
            {
                instance.Images.Banner = file;
                instance.Images.BannerAuto = false;
            }
            else
                instance.Images.Icon = file;
        }
        await SaveAsync(instance, ct);
    }

    public string? ImagePath(string id, bool banner)
    {
        if (!Text.IsValidId(id))
            return null;
        var path = Path.Combine(paths.Instance(id), banner ? "banner.png" : "icon.png");
        return File.Exists(path) ? path : null;
    }

    private void Publish() => events.Publish("instances.changed");
}

public static class ImageFormats
{
    public static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length > 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    public static bool IsJpeg(ReadOnlySpan<byte> data) => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    public static bool IsPngOrJpeg(ReadOnlySpan<byte> data) => IsPng(data) || IsJpeg(data);

    /// <summary>Breite und Höhe aus dem PNG-Header (IHDR), ohne das Bild zu dekodieren.</summary>
    public static (int Width, int Height)? PngSize(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data) || data.Length < 24)
            return null;
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[16..20]),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[20..24]));
    }

    public static string MimeOf(ReadOnlySpan<byte> data) => IsPng(data) ? "image/png" : IsJpeg(data) ? "image/jpeg" : "application/octet-stream";
}
