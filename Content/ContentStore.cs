namespace AxoClient.Content;

public class ContentStore(Installation inst, ModrinthClient modrinth)
{
    private const string DisabledSuffix = ".disabled";
    private const string IndexFileName = "launcher-content.json";
    private const string IconCacheFolder = "launcher-icons";

    public static readonly string[] ReservedPaths = [IndexFileName, IconCacheFolder + "/"];

    public record PlannedInstall(ContentType Type, string ProjectId, string Title, ContentVersion Version, bool Enabled = true);

    public record PinnedContent(ContentType Type, string ProjectId, string? VersionId, string Title, bool Enabled);

    public record PinnedResult(int Installed, List<string> Replaced, List<string> Failures);

    public Installation Installation => inst;

    private string IndexPath => inst.GameFile(IndexFileName);
    private string IconCacheDir => inst.GameFile(IconCacheFolder);

    public static string EnabledName(string fileName) =>
        fileName.EndsWith(DisabledSuffix) ? fileName[..^DisabledSuffix.Length] : fileName;

    public List<InstalledContent> GetIndex() => JsonFiles.Read<List<InstalledContent>>(IndexPath) ?? [];

    private void SaveIndex(List<InstalledContent> index) => JsonFiles.Write(IndexPath, index);

    public void AddIndexEntries(IEnumerable<InstalledContent> entries)
    {
        var index = GetIndex();
        foreach (var entry in entries)
        {
            index.RemoveAll(i => i.Type == entry.Type && i.FileName == entry.FileName);
            index.Add(entry);
        }
        SaveIndex(index);
    }

    private bool FileExists(ContentType type, string fileName)
    {
        var path = Path.Combine(inst.ContentDir(type), fileName);
        return File.Exists(path) || File.Exists(path + DisabledSuffix);
    }

    public int CountFiles(ContentType type)
    {
        var folder = inst.ContentDir(type);
        if (!Directory.Exists(folder))
            return 0;
        return type == ContentType.Mod
            ? Directory.GetFiles(folder, "*.jar*").Count(f => !BadgeMod.IsModFile(f))
            : Directory.GetFiles(folder, "*.zip").Length + Directory.GetDirectories(folder).Length;
    }

    public List<InstalledItem> GetInstalled(ContentType type)
    {
        var folder = inst.ContentDir(type);
        if (!Directory.Exists(folder))
            return [];

        var index = GetIndex();
        var entries = type == ContentType.Mod
            ? Directory.GetFiles(folder, "*.jar").Concat(Directory.GetFiles(folder, "*.jar" + DisabledSuffix))
            : Directory.GetFiles(folder, "*.zip").Concat(Directory.GetDirectories(folder));

        return entries
            .Where(path => !BadgeMod.IsModFile(path))
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var baseName = EnabledName(name);
                var known = index.FirstOrDefault(i => i.Type == type && i.FileName == baseName);
                return new InstalledItem
                {
                    FullPath = path,
                    FileName = baseName,
                    DisplayName = known?.Title ?? Path.GetFileNameWithoutExtension(baseName),
                    Enabled = name == baseName,
                    CanToggle = type == ContentType.Mod,
                    Type = type,
                    Entry = known,
                    Icon = ContentIcons.Load(path, type)
                };
            })
            .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task LoadMissingIconsAsync(IEnumerable<InstalledItem> items)
    {
        var pending = new List<InstalledItem>();
        foreach (var item in items.Where(i => i.Icon == null && i.Entry is { IsFromModrinth: true }))
        {
            var cached = CachedIconPath(item.Entry!.ProjectId);
            if (File.Exists(cached))
                item.Icon = ContentIcons.FromBytes(File.ReadAllBytes(cached));
            if (item.Icon == null)
                pending.Add(item);
        }
        if (pending.Count == 0)
            return;

        Dictionary<string, ProjectSummary> projects;
        try
        {
            projects = await modrinth.GetProjectsAsync(pending.Select(i => i.Entry!.ProjectId));
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Symbole der Inhalte abfragen", ex);
            return;
        }

        foreach (var item in pending)
        {
            if (!projects.TryGetValue(item.Entry!.ProjectId, out var project) || project.IconUrl == null)
                continue;
            try
            {
                var bytes = await modrinth.Http.GetByteArrayAsync(project.IconUrl);
                if (ContentIcons.FromBytes(bytes) is not { } image)
                    continue;
                item.Icon = image;
                Directory.CreateDirectory(IconCacheDir);
                await File.WriteAllBytesAsync(CachedIconPath(item.Entry.ProjectId), bytes);
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Symbol eines Inhalts laden", ex);
            }
        }
    }

    private string CachedIconPath(string projectId) => Path.Combine(IconCacheDir, projectId + ".png");

    public bool IsInstalled(string projectId) =>
        GetIndex().Any(i => i.IsFromModrinth && i.ProjectId == projectId && FileExists(i.Type, i.FileName));

    public bool HasModMatching(params string[] nameParts) =>
        GetInstalled(ContentType.Mod).Any(m => nameParts.Any(p =>
            m.DisplayName.Contains(p, StringComparison.OrdinalIgnoreCase)
            || m.FileName.Contains(p, StringComparison.OrdinalIgnoreCase)));

    public InstalledContent? CurrentOf(ContentType type, string projectId) =>
        GetIndex().FirstOrDefault(i => i.Type == type && i.IsFromModrinth && i.ProjectId == projectId);

    public async Task InstallAsync(string projectId, string title, ContentType type, IProgress<string> status,
        ContentVersion? version = null, bool replace = false, bool withDependencies = true,
        HashSet<string>? visited = null)
    {
        visited ??= [];
        if (!visited.Add(projectId) || (!replace && IsInstalled(projectId)))
            return;

        if (version == null)
        {
            status.Report($"Suche passende Version für {title}...");
            version = await modrinth.GetLatestVersionAsync(projectId, type, inst)
                      ?? throw new InvalidOperationException(
                          $"{title} hat keine Datei für Minecraft {inst.MinecraftVersion}" +
                          (type == ContentType.Mod ? $" mit {inst.Loader}." : "."));
        }
        if (version.DownloadUrl == null)
            throw new InvalidOperationException($"Der Autor von {title} erlaubt keine Downloads über andere Launcher.");

        status.Report($"Lade {title} {version.Name}...");
        var bytes = await modrinth.Http.GetByteArrayAsync(version.DownloadUrl);

        var folder = inst.ContentDir(type);
        Directory.CreateDirectory(folder);
        var index = GetIndex();
        var wasDisabled = false;
        foreach (var old in index.Where(i => i.Type == type && i.IsFromModrinth && i.ProjectId == projectId).ToList())
        {
            var oldPath = Path.Combine(folder, old.FileName);
            wasDisabled |= File.Exists(oldPath + DisabledSuffix);
            File.Delete(oldPath);
            File.Delete(oldPath + DisabledSuffix);
            index.Remove(old);
        }

        await File.WriteAllBytesAsync(Path.Combine(folder, version.FileName + (wasDisabled ? DisabledSuffix : "")), bytes);
        index.RemoveAll(i => i.Type == type && i.FileName == version.FileName);
        index.Add(InstalledContent.Create(type, title, version));
        SaveIndex(index);

        if (type != ContentType.Mod || !withDependencies)
            return;
        foreach (var dependencyId in version.RequiredProjectIds)
        {
            if (visited.Contains(dependencyId) || IsInstalled(dependencyId))
                continue;
            var (id, dependencyTitle) = await modrinth.GetProjectInfoAsync(dependencyId);
            await InstallAsync(id, dependencyTitle, ContentType.Mod, status, visited: visited);
        }
    }

    public async Task InstallBySlugAsync(string slug, ContentType type, IProgress<string> status)
    {
        var (id, title) = await modrinth.GetProjectInfoAsync(slug);
        await InstallAsync(id, title, type, status);
    }

    public async Task<List<string>> InstallManyAsync(IReadOnlyList<PlannedInstall> plans, WorkProgress progress,
        int parallel = 6)
    {
        var failures = new List<string>();
        var entries = new List<InstalledContent>();
        var done = 0;
        using var gate = new SemaphoreSlim(parallel);

        await Task.WhenAll(plans.Select(async plan =>
        {
            await gate.WaitAsync(progress.Cancel);
            try
            {
                progress.Cancel.ThrowIfCancellationRequested();
                var version = plan.Version;
                if (version.DownloadUrl == null)
                    throw new InvalidOperationException("Der Autor erlaubt keine Downloads über andere Launcher.");
                var name = Path.GetFileName(version.FileName);
                if (name.Length == 0 || name != version.FileName || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidOperationException("Ungültiger Dateiname.");

                var target = Path.Combine(inst.ContentDir(plan.Type), name + (plan.Enabled ? "" : DisabledSuffix));
                await HttpDownloads.DownloadToFileAsync(modrinth.Http, version.DownloadUrl, target, null, progress.Cancel);
                lock (entries)
                    entries.Add(InstalledContent.Create(plan.Type, plan.Title, version, name));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lock (failures)
                    failures.Add($"{plan.Title} ({ErrorReport.Short(ex)})");
            }
            finally
            {
                gate.Release();
                var finished = Interlocked.Increment(ref done);
                progress.Text.Report($"Lade Inhalte... {finished} von {plans.Count}");
                progress.Fraction.Report((double)finished / plans.Count);
            }
        }));

        if (entries.Count > 0)
            AddIndexEntries(entries);
        return failures;
    }

    public async Task<PinnedResult> InstallPinnedAsync(IReadOnlyList<PinnedContent> wanted, WorkProgress progress)
    {
        progress.Text.Report("Suche die Versionen bei Modrinth...");
        var pinned = await modrinth.GetVersionsByIdsAsync(wanted.Where(e => e.VersionId != null).Select(e => e.VersionId!));

        var exact = new List<PlannedInstall>();
        var newest = new List<PinnedContent>();
        foreach (var entry in wanted)
        {
            if (entry.VersionId != null && pinned.TryGetValue(entry.VersionId, out var version)
                && version.ProjectId == entry.ProjectId && version.Supports(inst, entry.Type))
                exact.Add(new PlannedInstall(entry.Type, entry.ProjectId, entry.Title, version,
                    entry.Enabled || entry.Type != ContentType.Mod));
            else
                newest.Add(entry);
        }

        var failures = exact.Count > 0 ? await InstallManyAsync(exact, progress) : [];
        var installed = exact.Count - failures.Count;
        var replaced = new List<string>();
        for (var i = 0; i < newest.Count; i++)
        {
            var entry = newest[i];
            progress.Cancel.ThrowIfCancellationRequested();
            progress.Text.Report($"Lade {entry.Title} ({i + 1} von {newest.Count}, neueste Version)...");
            try
            {
                await InstallAsync(entry.ProjectId, entry.Title, entry.Type, progress.Text);
                if (!entry.Enabled && entry.Type == ContentType.Mod
                    && GetInstalled(ContentType.Mod).FirstOrDefault(m => m.Entry?.ProjectId == entry.ProjectId) is { } item)
                    SetEnabled(item, false);
                replaced.Add(entry.Title);
                installed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{entry.Title} ({ErrorReport.Short(ex)})");
            }
        }
        return new PinnedResult(installed, replaced, failures);
    }

    public async Task<int> IdentifyUnknownAsync(ContentType type)
    {
        var unknown = GetInstalled(type).Where(i => i.Entry == null && File.Exists(i.FullPath)).ToList();
        if (unknown.Count == 0)
            return 0;

        var hashes = unknown.ToDictionary(i => i, i => FileOps.Sha1(i.FullPath));
        var found = await modrinth.LookupByHashAsync(hashes.Values);
        var projects = found.Count == 0 ? [] : await modrinth.GetProjectsAsync(found.Values.Select(v => v.ProjectId));
        var entries = new List<InstalledContent>();
        foreach (var (item, hash) in hashes)
        {
            if (!found.TryGetValue(hash, out var version))
                continue;
            var title = projects.TryGetValue(version.ProjectId, out var project) && project.Title.Length > 0
                ? project.Title
                : item.DisplayName;
            entries.Add(InstalledContent.Create(type, title, version, item.FileName));
        }
        if (entries.Count > 0)
            AddIndexEntries(entries);
        return entries.Count;
    }

    public async Task<int> CheckUpdatesAsync(IReadOnlyList<InstalledItem> items)
    {
        await Task.WhenAll(items.Where(i => i.Entry is { IsFromModrinth: true }).Select(async item =>
        {
            var entry = item.Entry!;
            try
            {
                var latest = await modrinth.GetLatestVersionAsync(entry.ProjectId, item.Type, inst);
                var isNewer = latest != null
                              && latest.Id != entry.VersionId
                              && latest.FileName != entry.FileName
                              && (entry.VersionDate == null || latest.Date > entry.VersionDate);
                item.Update = isNewer ? latest : null;
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Update-Suche für \"" + item.DisplayName + "\"", ex);
                item.Update = null;
            }
        }));
        return items.Count(i => i.HasUpdate);
    }

    public async Task ApplyUpdateAsync(InstalledItem item, IProgress<string> status)
    {
        if (item is not { Entry: { IsFromModrinth: true } entry, Update: { } update })
            return;
        await InstallAsync(entry.ProjectId, entry.Title, item.Type, status, version: update, replace: true);
        item.Update = null;
    }

    public void SetEnabled(InstalledItem item, bool enabled)
    {
        if (!item.CanToggle || item.Enabled == enabled)
            return;
        var dir = Path.GetDirectoryName(item.FullPath)!;
        File.Move(item.FullPath, Path.Combine(dir, enabled ? item.FileName : item.FileName + DisabledSuffix), overwrite: true);
    }

    public void Delete(InstalledItem item)
    {
        if (Directory.Exists(item.FullPath))
            Directory.Delete(item.FullPath, recursive: true);
        else
            File.Delete(item.FullPath);

        var index = GetIndex();
        if (index.RemoveAll(i => i.FileName == item.FileName) > 0)
            SaveIndex(index);
    }
}
