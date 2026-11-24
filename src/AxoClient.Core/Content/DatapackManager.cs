using AxoClient.Core.Common;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Content;

/// <summary>Ein Datapack einer Welt, wie ihn die UI-Liste anzeigt.</summary>
public sealed record DatapackItem(
    string File,
    string Name,
    string? Version,
    bool Enabled,
    bool IsFolder,
    ContentSource Source,
    string? ProjectId,
    string? IconUrl,
    string? Author,
    long Size);

/// <summary>
/// <c>instances/&lt;id&gt;/datapacks.json</c>: Herkunft der Datapacks je Welt. Die Dateien selbst liegen in der Welt,
/// damit Minecraft sie findet; dort soll keine fremde Datei liegen.
/// </summary>
public sealed class DatapackDocument : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public Dictionary<string, List<ContentEntry>> Worlds { get; set; } = [];
}

/// <summary>
/// Datapacks gehören zu einer Welt (<c>saves/&lt;welt&gt;/datapacks</c>), nicht zur Instanz. Minecraft lädt dort
/// jede <c>.zip</c> und jeden Ordner mit <c>pack.mcmeta</c>; deaktiviert heißt hier <c>.zip.disabled</c>.
/// Neu gefundene Packs schaltet Minecraft beim nächsten Laden der Welt selbst ein.
/// </summary>
public sealed class DatapackManager(AppPaths paths, InstanceManager instances, WorldManager worlds, ContentProviders providers,
    DownloadManager downloads)
{
    private const string Disabled = ".disabled";

    // Eine Store-Instanz je Instanz, damit ihre Sperre gleichzeitige Änderungen ordnet
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, JsonStore<DatapackDocument>> _stores = new();

    private JsonStore<DatapackDocument> Store(string instanceId) => _stores.GetOrAdd(Text.RequireId(instanceId),
        id => new JsonStore<DatapackDocument>(paths.Fs, Path.Combine(paths.Instance(id), "datapacks.json")));

    private string Folder(string instanceId, string world) => Path.Combine(worlds.WorldDir(instanceId, world), "datapacks");

    public async Task<IReadOnlyList<DatapackItem>> ListAsync(string instanceId, string world, CancellationToken ct = default)
    {
        var dir = Folder(instanceId, world);
        var known = (await Store(instanceId).LoadAsync(ct)).Worlds.GetValueOrDefault(world) ?? [];
        if (!Directory.Exists(dir))
            return [];
        var items = new List<DatapackItem>();
        foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            var isFolder = info is DirectoryInfo;
            if (isFolder && !File.Exists(Path.Combine(info.FullName, "pack.mcmeta")))
                continue;
            bool enabled;
            string file;
            if (isFolder)
                (file, enabled) = (info.Name, true);
            else if (info.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                (file, enabled) = (info.Name, true);
            else if (info.Name.EndsWith(".zip" + Disabled, StringComparison.OrdinalIgnoreCase))
                (file, enabled) = (info.Name[..^Disabled.Length], false);
            else
                continue;
            var entry = known.FirstOrDefault(e => e.File.Equals(file, StringComparison.OrdinalIgnoreCase));
            var name = entry?.Name ?? (isFolder ? file : Path.GetFileNameWithoutExtension(file));
            items.Add(new DatapackItem(file, name, entry?.Version, enabled, isFolder, entry?.Source ?? ContentSource.Local, entry?.ProjectId,
                entry?.IconUrl, entry?.Author, isFolder ? WorldManager.DirectorySize(info.FullName) : ((FileInfo)info).Length));
        }
        return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Passende Version (Datapack-Loader, Minecraft-Version der Instanz) laden; ersetzt eine ältere desselben Projekts.</summary>
    public async Task<string> InstallAsync(string instanceId, string world, InstallRequest request, JobContext job)
    {
        var dir = Folder(instanceId, world);
        var instance = await instances.GetAsync(instanceId, job.Token);
        var provider = providers.Get(request.Source);
        job.Status("content.resolving");
        ContentVersion? version;
        if (request.VersionId != null)
            version = await provider.GetVersionAsync(request.VersionId, job.Token);
        else
        {
            var list = await provider.GetVersionsAsync(request.ProjectId, instance.Minecraft.Version, null, ProjectKind.DataPack, job.Token);
            version = list.FirstOrDefault(v => v.Channel == "release") ?? list.FirstOrDefault();
        }
        if (version == null)
            throw new AxoException("content.noCompatibleVersion", new() { ["version"] = instance.Minecraft.Version });
        // Manche Projekte haben neben der Zip auch eine Mod-Jar: für die Welt zählt nur die Zip
        var file = version.Files.FirstOrDefault(f => f.Primary && IsZip(f.FileName)) ?? version.Files.FirstOrDefault(f => IsZip(f.FileName))
                   ?? throw new AxoException("datapacks.noZip");
        if (string.IsNullOrEmpty(file.Url))
            throw new AxoException("content.manualDownload", new() { ["name"] = version.Name, ["projectId"] = version.ProjectId });
        if (!PathGuard.IsSafeFileName(file.FileName))
            throw new AxoException("errors.invalidFileName");

        Directory.CreateDirectory(dir);
        job.Status("content.downloading");
        await downloads.DownloadAllAsync([new DownloadRequest(file.Url, PathGuard.Combine(dir, file.FileName), file.Sha1, file.Sha512,
            file.Size > 0 ? file.Size : null)], job.Range(0.05, 0.95), job.Token);

        var project = (await provider.GetProjectsAsync([version.ProjectId], job.Token)).GetValueOrDefault(version.ProjectId);
        await Store(instanceId).UpdateAsync(doc =>
        {
            var entries = doc.Worlds.TryGetValue(world, out var l) ? l : doc.Worlds[world] = [];
            // Ältere Version desselben Projekts entfernen, sonst lädt Minecraft beide
            foreach (var old in entries.Where(e => e.ProjectId == version.ProjectId && !e.File.Equals(file.FileName, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                TryDelete(Path.Combine(dir, old.File));
                TryDelete(Path.Combine(dir, old.File + Disabled));
                entries.Remove(old);
            }
            entries.RemoveAll(e => e.File.Equals(file.FileName, StringComparison.OrdinalIgnoreCase));
            entries.Add(new ContentEntry
            {
                File = file.FileName,
                Name = project?.Title ?? version.Name,
                Version = version.VersionNumber,
                Source = request.Source == "curseforge" ? ContentSource.CurseForge : ContentSource.Modrinth,
                ProjectId = version.ProjectId,
                VersionId = version.Id,
                Hashes = new ContentHashes(file.Sha1, file.Sha512),
                Size = file.Size,
                IconUrl = project?.IconUrl,
                Author = project?.Author is { Length: > 0 } a ? a : null
            });
        }, job.Token);
        return file.FileName;
    }

    public Task SetEnabledAsync(string instanceId, string world, string file, bool enabled)
    {
        var dir = Folder(instanceId, world);
        var name = RequireFile(file);
        if (Directory.Exists(Path.Combine(dir, name)))
            throw new AxoException("datapacks.folderToggle");
        var on = Path.Combine(dir, name);
        var off = on + Disabled;
        var from = enabled ? off : on;
        if (!File.Exists(from))
            return Task.CompletedTask;
        try
        {
            File.Move(from, enabled ? on : off, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AxoException("content.fileInUse", new() { ["name"] = name }, ex);
        }
        return Task.CompletedTask;
    }

    /// <summary>In den Papierkorb, wie bei Mods.</summary>
    public async Task RemoveAsync(string instanceId, string world, string file, CancellationToken ct = default)
    {
        var dir = Folder(instanceId, world);
        var name = RequireFile(file);
        var folder = Path.Combine(dir, name);
        if (Directory.Exists(folder))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        foreach (var path in new[] { folder, folder + Disabled }.Where(File.Exists))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        await Store(instanceId).UpdateAsync(doc =>
        {
            if (doc.Worlds.TryGetValue(world, out var entries))
                entries.RemoveAll(e => e.File.Equals(name, StringComparison.OrdinalIgnoreCase));
        }, ct);
    }

    private static bool IsZip(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static string RequireFile(string? file) =>
        PathGuard.IsSafeFileName(file) ? file! : throw new AxoException("errors.invalidFileName");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException ex)
        {
            AppLog.Warn($"Alter Datapack konnte nicht gelöscht werden: {Path.GetFileName(path)}", ex);
        }
    }
}
