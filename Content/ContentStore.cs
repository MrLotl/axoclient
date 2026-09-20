using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McLauncher;

/// <summary>Merkt sich, welche Datei von welchem Projekt und welcher Version stammt.</summary>
public class InstalledContent
{
    public string FileName { get; set; } = "";
    public ContentType Type { get; set; }
    public ContentSource Source { get; set; }
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? VersionId { get; set; }
    public string? VersionName { get; set; }
    public DateTime? VersionDate { get; set; }
}

/// <summary>Eine Datei im mods-, resourcepacks- oder shaderpacks-Ordner.</summary>
public class InstalledItem : INotifyPropertyChanged
{
    public required string FullPath { get; init; }
    public required string DisplayName { get; init; }
    public required string FileName { get; init; }
    public required bool Enabled { get; init; }
    public required bool CanToggle { get; init; }
    public required ContentType Type { get; init; }

    /// <summary>Herkunft (null = manuell hinzugefügt, Quelle unbekannt).</summary>
    public InstalledContent? Entry { get; init; }

    private System.Windows.Media.Imaging.BitmapSource? _icon;

    /// <summary>
    /// Profilbild: zuerst aus der Datei selbst, sonst später vom Anbieter nachgeladen
    /// (Shader und viele Ressourcenpakete haben kein Bild in der Datei).
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public bool CanChangeVersion => Entry != null;

    /// <summary>Teilen geht nur, wenn Modrinth die Datei kennt: der Empfänger lädt sie von dort.</summary>
    public bool CanShare => Entry is { Source: ContentSource.Modrinth };

    public string SourceText => Entry == null
        ? $"Manuell · {FileName}"
        : $"{Entry.Source} · {Entry.VersionName ?? FileName}";

    private ContentVersion? _update;

    /// <summary>Neuere passende Version, falls vorhanden (nach "Updates suchen").</summary>
    public ContentVersion? Update
    {
        get => _update;
        set
        {
            _update = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Update)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUpdate)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateText)));
        }
    }

    public bool HasUpdate => Update != null;
    public string UpdateText => Update == null ? "" : $"Update: {Update.Name}";

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Verwaltet Mods, Ressourcenpakete und Shader einer Installation.</summary>
public class ContentStore(Installation inst, HttpClient http)
{
    private const string DisabledSuffix = ".disabled";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Installation Installation => inst;

    private string IndexPath => Path.Combine(inst.GameDir, "launcher-content.json");

    public string FolderOf(ContentType type) =>
        Path.Combine(inst.GameDir, ContentTypes.Folder(type, inst.MinecraftVersion));

    private List<InstalledContent> LoadIndex()
    {
        try
        {
            return JsonSerializer.Deserialize<List<InstalledContent>>(File.ReadAllText(IndexPath), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveIndex(List<InstalledContent> index)
    {
        Directory.CreateDirectory(inst.GameDir);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, JsonOptions));
    }

    /// <summary>Herkunft aller über den Launcher installierten Dateien (Quelle, Projekt, Titel, Version).</summary>
    public List<InstalledContent> GetIndex() => LoadIndex();

    /// <summary>Übernimmt Herkunftseinträge (z.B. beim Übertragen aus einer anderen Instanz).</summary>
    public void AddIndexEntries(IEnumerable<InstalledContent> entries)
    {
        var index = LoadIndex();
        foreach (var entry in entries)
        {
            index.RemoveAll(i => i.Type == entry.Type && i.FileName == entry.FileName);
            index.Add(entry);
        }
        SaveIndex(index);
    }

    private bool FileExists(ContentType type, string fileName)
    {
        var path = Path.Combine(FolderOf(type), fileName);
        return File.Exists(path) || File.Exists(path + DisabledSuffix);
    }

    public List<InstalledItem> GetInstalled(ContentType type)
    {
        var folder = FolderOf(type);
        if (!Directory.Exists(folder))
            return [];

        var index = LoadIndex();
        var entries = type == ContentType.Mod
            ? Directory.GetFiles(folder, "*.jar").Concat(Directory.GetFiles(folder, "*.jar" + DisabledSuffix))
            : Directory.GetFiles(folder, "*.zip").Concat(Directory.GetDirectories(folder)); // Pakete können auch Ordner sein

        return entries
            .Where(path => !Path.GetFileName(path).StartsWith(Badge.ModFileName)) // vom Launcher verwaltet
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var enabled = !name.EndsWith(DisabledSuffix);
                var baseName = enabled ? name : name[..^DisabledSuffix.Length];
                var known = index.FirstOrDefault(i => i.Type == type && i.FileName == baseName);
                return new InstalledItem
                {
                    FullPath = path,
                    FileName = baseName,
                    DisplayName = known?.Title ?? Path.GetFileNameWithoutExtension(baseName),
                    Enabled = enabled,
                    CanToggle = type == ContentType.Mod, // Pakete werden im Spiel selbst aktiviert
                    Type = type,
                    Entry = known,
                    Icon = ContentIcons.Load(path, type)
                };
            })
            .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Ordner für nachgeladene Profilbilder (Shader und viele Pakete bringen keines mit).</summary>
    private string IconCacheDir => Path.Combine(inst.GameDir, "launcher-icons");

    private string CachedIconPath(string projectId) => Path.Combine(IconCacheDir, projectId + ".png");

    /// <summary>
    /// Holt fehlende Profilbilder von Modrinth nach und legt sie neben der Instanz ab, damit sie
    /// beim nächsten Mal sofort da sind. Fehler werden übergangen (dann bleibt das Ersatzsymbol stehen).
    /// </summary>
    public async Task LoadMissingIconsAsync(IEnumerable<InstalledItem> items, ModrinthProvider modrinth)
    {
        var pending = new List<InstalledItem>();
        foreach (var item in items.Where(i => i.Icon == null && i.Entry is { Source: ContentSource.Modrinth }))
        {
            var cached = CachedIconPath(item.Entry!.ProjectId);
            if (File.Exists(cached))
                item.Icon = ContentIcons.FromBytes(File.ReadAllBytes(cached));
            if (item.Icon == null)
                pending.Add(item);
        }
        if (pending.Count == 0)
            return;

        Dictionary<string, string> urls;
        try
        {
            urls = await modrinth.GetIconUrlsAsync(pending.Select(i => i.Entry!.ProjectId));
        }
        catch
        {
            return; // offline oder Modrinth gerade nicht erreichbar
        }

        foreach (var item in pending)
        {
            if (!urls.TryGetValue(item.Entry!.ProjectId, out var url))
                continue;
            try
            {
                var bytes = await http.GetByteArrayAsync(url);
                if (ContentIcons.FromBytes(bytes) is not { } image)
                    continue; // z.B. webp, das Windows nicht anzeigen kann
                item.Icon = image;
                Directory.CreateDirectory(IconCacheDir);
                await File.WriteAllBytesAsync(CachedIconPath(item.Entry.ProjectId), bytes);
            }
            catch
            {
                // einzelnes Bild fehlt: nicht schlimm
            }
        }
    }

    public bool IsInstalled(ContentSource source, string projectId) =>
        LoadIndex().Any(i => i.Source == source && i.ProjectId == projectId && FileExists(i.Type, i.FileName));

    public bool HasModMatching(params string[] nameParts) =>
        GetInstalled(ContentType.Mod).Any(m => nameParts.Any(p =>
            m.DisplayName.Contains(p, StringComparison.OrdinalIgnoreCase)
            || m.FileName.Contains(p, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Lädt eine Version herunter (ohne Angabe: die neueste passende). Mit <paramref name="replace"/> wird eine
    /// bereits installierte Version desselben Projekts ersetzt (Version ändern / Update).
    /// Bei Mods werden benötigte Abhängigkeiten mitinstalliert.
    /// </summary>
    public async Task InstallAsync(IContentProvider provider, string projectId, string title, ContentType type,
        IProgress<string> status, HashSet<string>? visited = null, ContentVersion? version = null, bool replace = false,
        bool withDependencies = true)
    {
        visited ??= [];
        if (!visited.Add(projectId) || (!replace && IsInstalled(provider.Source, projectId)))
            return;

        if (version == null)
        {
            status.Report($"Suche passende Version für {title}...");
            version = await provider.GetLatestVersionAsync(projectId, type, inst)
                      ?? throw new InvalidOperationException(
                          $"{title} hat keine Datei für Minecraft {inst.MinecraftVersion}" +
                          (type == ContentType.Mod ? $" mit {inst.Loader}." : "."));
        }
        if (version.DownloadUrl == null)
            throw new InvalidOperationException(
                $"Der Autor von {title} erlaubt keine Downloads über andere Launcher. " +
                "Bitte über die CurseForge-Webseite herunterladen und in den Ordner legen.");

        status.Report($"Lade {title} {version.Name}...");
        var bytes = await http.GetByteArrayAsync(version.DownloadUrl);

        // Bisherige Datei(en) desselben Projekts entfernen; war sie deaktiviert, bleibt die neue es auch
        var folder = FolderOf(type);
        Directory.CreateDirectory(folder);
        var index = LoadIndex();
        var wasDisabled = false;
        foreach (var old in index.Where(i => i.Type == type && i.Source == provider.Source && i.ProjectId == projectId).ToList())
        {
            var oldPath = Path.Combine(folder, old.FileName);
            wasDisabled |= File.Exists(oldPath + DisabledSuffix);
            File.Delete(oldPath);
            File.Delete(oldPath + DisabledSuffix);
            index.Remove(old);
        }

        var target = Path.Combine(folder, version.FileName + (wasDisabled ? DisabledSuffix : ""));
        await File.WriteAllBytesAsync(target, bytes);

        index.RemoveAll(i => i.Type == type && i.FileName == version.FileName);
        index.Add(new InstalledContent
        {
            FileName = version.FileName,
            Type = type,
            Source = provider.Source,
            ProjectId = projectId,
            Title = title,
            VersionId = version.Id,
            VersionName = version.Name,
            VersionDate = version.Date
        });
        SaveIndex(index);

        if (type != ContentType.Mod || !withDependencies)
            return;
        foreach (var dependencyId in version.RequiredProjectIds)
        {
            if (visited.Contains(dependencyId) || IsInstalled(provider.Source, dependencyId))
                continue;
            var (id, depTitle) = await provider.GetProjectInfoAsync(dependencyId);
            await InstallAsync(provider, id, depTitle, ContentType.Mod, status, visited);
        }
    }

    /// <summary>Eine bereits aufgelöste Version, die installiert werden soll (siehe <see cref="InstallManyAsync"/>).</summary>
    public record PlannedInstall(ContentType Type, string ProjectId, string Title, ContentVersion Version, bool Enabled = true);

    /// <summary>
    /// Lädt viele bereits bekannte Versionen parallel herunter. Anders als <see cref="InstallAsync"/> löst das keine
    /// Abhängigkeiten auf und ersetzt nichts Vorhandenes; gedacht für eine frische Instanz. Dass die Herkunftsliste
    /// erst am Ende einmal geschrieben wird, macht paralleles Laden erst sicher.
    /// Fehler einzelner Dateien brechen nicht ab, sondern kommen als Text zurück.
    /// </summary>
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
                // Der Dateiname kommt von Modrinth, wird aber trotzdem nicht blind als Pfad benutzt
                var name = Path.GetFileName(version.FileName);
                if (name.Length == 0 || name != version.FileName || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidOperationException("Ungültiger Dateiname.");

                var target = Path.Combine(FolderOf(plan.Type), name + (plan.Enabled ? "" : DisabledSuffix));
                await Downloads.DownloadToFileAsync(http, version.DownloadUrl, target, null, progress.Cancel);
                lock (entries)
                    entries.Add(new InstalledContent
                    {
                        FileName = name,
                        Type = plan.Type,
                        Source = ContentSource.Modrinth,
                        ProjectId = plan.ProjectId,
                        Title = plan.Title,
                        VersionId = version.Id,
                        VersionName = version.Name,
                        VersionDate = version.Date
                    });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lock (failures)
                    failures.Add($"{plan.Title} ({ex.Message})");
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

    /// <summary>Installiert ein Projekt über seinen Kurznamen (z.B. "iris" bei Modrinth).</summary>
    public async Task InstallBySlugAsync(IContentProvider provider, string slug, ContentType type, IProgress<string> status)
    {
        var (id, title) = await provider.GetProjectInfoAsync(slug);
        await InstallAsync(provider, id, title, type, status);
    }

    /// <summary>
    /// Ordnet manuell hinzugefügte Dateien über ihren SHA-1 einem Modrinth-Projekt zu,
    /// damit auch für sie Versionswechsel, Updates und die Reparatur funktionieren.
    /// </summary>
    public async Task<int> IdentifyUnknownAsync(ContentType type, ModrinthProvider modrinth)
    {
        var unknown = GetInstalled(type).Where(i => i.Entry == null && File.Exists(i.FullPath)).ToList();
        if (unknown.Count == 0)
            return 0;

        var hashes = unknown.ToDictionary(i => i, i => Sha1(i.FullPath));
        var found = await modrinth.LookupByHashAsync(hashes.Values);
        // Namen in einem Rutsch holen statt pro Datei eine eigene Anfrage (bei großen Modpacks sonst hunderte)
        var titles = found.Count == 0
            ? []
            : await modrinth.GetProjectTitlesAsync(found.Values.Select(v => v.ProjectId));
        var entries = new List<InstalledContent>();
        foreach (var (item, hash) in hashes)
        {
            if (!found.TryGetValue(hash, out var version))
                continue;
            entries.Add(new InstalledContent
            {
                FileName = item.FileName,
                Type = type,
                Source = ContentSource.Modrinth,
                ProjectId = version.ProjectId,
                Title = titles.TryGetValue(version.ProjectId, out var title) && title.Length > 0 ? title : item.DisplayName,
                VersionId = version.Id,
                VersionName = version.Name,
                VersionDate = version.Date
            });
        }
        if (entries.Count > 0)
            AddIndexEntries(entries);
        return entries.Count;
    }

    /// <summary>
    /// Sucht für alle Einträge bekannter Herkunft eine neuere passende Version und setzt <see cref="InstalledItem.Update"/>.
    /// </summary>
    public async Task<int> CheckUpdatesAsync(IEnumerable<InstalledItem> items, Func<ContentSource, IContentProvider?> providerFor)
    {
        var checks = items.Where(i => i.Entry != null).Select(async item =>
        {
            var entry = item.Entry!;
            if (providerFor(entry.Source) is not { } provider)
                return;
            try
            {
                var latest = await provider.GetLatestVersionAsync(entry.ProjectId, item.Type, inst);
                var isNewer = latest != null
                              && latest.Id != entry.VersionId
                              && latest.FileName != entry.FileName
                              && (entry.VersionDate == null || latest.Date > entry.VersionDate);
                item.Update = isNewer ? latest : null;
            }
            catch
            {
                item.Update = null; // Projekt nicht erreichbar, beim nächsten Mal erneut prüfen
            }
        });
        await Task.WhenAll(checks);
        return items.Count(i => i.HasUpdate);
    }

    public static string Sha1(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    public void SetEnabled(InstalledItem item, bool enabled)
    {
        if (!item.CanToggle || item.Enabled == enabled)
            return;
        var dir = Path.GetDirectoryName(item.FullPath)!;
        var target = Path.Combine(dir, enabled ? item.FileName : item.FileName + DisabledSuffix);
        File.Move(item.FullPath, target, overwrite: true); // ggf. veraltete Kopie mit dem anderen Zustand ersetzen
    }

    public void Delete(InstalledItem item)
    {
        if (Directory.Exists(item.FullPath))
            Directory.Delete(item.FullPath, recursive: true);
        else
            File.Delete(item.FullPath);

        var index = LoadIndex();
        if (index.RemoveAll(i => i.FileName == item.FileName) > 0)
            SaveIndex(index);
    }
}
