using System.IO.Compression;
using System.Text.Json;
using AxoClient.Core.Common;
using AxoClient.Core.Content;

namespace AxoClient.Core.Instances;

/// <summary>Manifest im Backup-ZIP (<c>axoclient-backup.json</c>).</summary>
public sealed class BackupManifest : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public string App { get; set; } = "AxoClient";
    public string AppVersion { get; set; } = AppInfo.Version;
    public string InstanceName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Ganze Instanz als <c>.zip</c> sichern (Welten, Screenshots, instance.json, content.json, playtime.json) und wieder
/// einlesen. Logs und Crash-Reports kommen nicht mit. Beim Import gelten die Sicherheitsregeln: Pfadprüfung,
/// keine Java-Argumente übernehmen.
/// </summary>
public sealed class InstanceBackup(AppPaths paths, InstanceManager instances, ContentManager content, ModpackManager modpacks)
{
    private const string ManifestName = "axoclient-backup.json";

    public async Task ExportAsync(string instanceId, string targetPath, JobContext job)
    {
        var instance = await instances.GetAsync(instanceId, job.Token);
        var root = paths.Instance(instanceId);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(r => !r.StartsWith("backups/") && !InstanceManager.IsTransient(r.StartsWith("minecraft/") ? r["minecraft/".Length..] : r))
            .ToList();
        var tmp = targetPath + ".tmp";
        await using (var stream = File.Create(tmp))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var manifest = zip.CreateEntry(ManifestName);
            await using (var writer = manifest.Open())
                await JsonSerializer.SerializeAsync(writer, new BackupManifest { InstanceName = instance.Name }, Json.Options, job.Token);
            for (var i = 0; i < files.Count; i++)
            {
                job.Token.ThrowIfCancellationRequested();
                var entry = zip.CreateEntry(files[i], CompressionLevel.Fastest);
                await using var source = new FileStream(Path.Combine(root, files[i]), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await using var target = entry.Open();
                await source.CopyToAsync(target, job.Token);
                job.Report((double)(i + 1) / files.Count);
            }
        }
        File.Move(tmp, targetPath, overwrite: true);
    }

    /// <summary>Erkennt AxoClient-Backup oder .mrpack und liest beides als neue Instanz ein.</summary>
    public async Task<Instance> ImportAsync(string archivePath, JobContext job)
    {
        if (archivePath.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            return await modpacks.ImportMrpackAsync(archivePath, job);

        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.GetEntry("modrinth.index.json") != null)
            return await modpacks.ImportMrpackAsync(archivePath, job);
        var manifestEntry = zip.GetEntry(ManifestName) ?? throw new AxoException("import.unknownFormat");
        await using (var _ = manifestEntry.Open())
        {
        }
        var instanceEntry = zip.GetEntry("instance.json") ?? throw new AxoException("import.unknownFormat");
        Instance source;
        await using (var s = instanceEntry.Open())
            source = await JsonSerializer.DeserializeAsync<Instance>(s, Json.Options, job.Token) ?? throw new AxoException("import.unknownFormat");

        var instance = await instances.CreateAsync(new CreateInstanceRequest(InstanceRules.CleanName(source.Name), source.Minecraft.Version,
            source.Loader.Type, Game.LoaderManager.IsSafeVersion(source.Loader.Version) ? source.Loader.Version : null,
            source.MemoryMb), OriginType.Import, job.Token);
        var root = paths.Instance(instance.Id);
        var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/') && e.FullName is not (ManifestName or "instance.json")).ToList();
        try
        {
            for (var i = 0; i < entries.Count; i++)
            {
                job.Token.ThrowIfCancellationRequested();
                var target = PathGuard.Combine(root, entries[i].FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var from = entries[i].Open();
                await using var to = File.Create(target);
                await from.CopyToAsync(to, job.Token);
                job.Report((double)(i + 1) / entries.Count);
            }
        }
        catch
        {
            await instances.DeleteAsync(instance.Id);
            throw;
        }

        // Java-Argumente aus fremden Archiven nie übernehmen; Bilder nur, wenn sie wirklich Bilder sind
        instance = await instances.GetAsync(instance.Id, job.Token);
        instance.JavaArgs = "";
        instance.Minecraft = source.Minecraft;
        instance.AxoMod = source.AxoMod;
        instance.ModLock = new ModLock { Level = source.ModLock.Level, PackMods = [.. source.ModLock.PackMods.Take(5000)] };
        instance.Images = new InstanceImages
        {
            Icon = File.Exists(Path.Combine(root, "icon.png")) && ImageFormats.IsPngOrJpeg(await File.ReadAllBytesAsync(Path.Combine(root, "icon.png"), job.Token)) ? "icon.png" : null,
            Banner = File.Exists(Path.Combine(root, "banner.png")) ? "banner.png" : null,
            BannerAuto = source.Images.BannerAuto
        };
        await instances.SaveAsync(instance, job.Token);
        await content.SyncAsync(instance.Id, job.Token);
        return instance;
    }
}
