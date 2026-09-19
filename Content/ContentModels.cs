using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace McLauncher;

public enum ContentType
{
    Mod,
    ResourcePack,
    Shader
}

public enum ContentSource
{
    Modrinth,
    CurseForge
}

public static class ContentTypes
{
    public static string Folder(ContentType type, string mcVersion) => type switch
    {
        ContentType.Mod => "mods",
        ContentType.ResourcePack => UsesTexturePacks(mcVersion) ? "texturepacks" : "resourcepacks",
        _ => "shaderpacks"
    };

    /// <summary>
    /// Vor 1.6 hießen Ressourcenpakete "Texture Packs" und lagen im Ordner "texturepacks".
    /// Betrifft Alpha/Beta, Releases 1.0 bis 1.5.2 und Snapshots vor 13w24a.
    /// </summary>
    public static bool UsesTexturePacks(string v)
    {
        if (v.StartsWith("a") || v.StartsWith("b") || v.StartsWith("c") || v.StartsWith("rd-") || v.StartsWith("inf-"))
            return true;

        var release = System.Text.RegularExpressions.Regex.Match(v, @"^1\.(\d+)");
        if (release.Success)
            return int.Parse(release.Groups[1].Value) < 6;

        var snapshot = System.Text.RegularExpressions.Regex.Match(v, @"^(\d\d)w(\d\d)");
        if (snapshot.Success)
        {
            int year = int.Parse(snapshot.Groups[1].Value), week = int.Parse(snapshot.Groups[2].Value);
            return year < 13 || (year == 13 && week < 24);
        }
        return false; // neue Versionsnamen wie 26.2
    }
}

/// <summary>Ein Suchergebnis (Mod, Ressourcenpaket oder Shader) von Modrinth oder CurseForge.</summary>
public class ContentProject : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required ContentSource Source { get; init; }
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string? IconUrl { get; init; }
    public long Downloads { get; init; }
    public string? WebsiteUrl { get; init; }

    public string DownloadsText => Downloads switch
    {
        >= 1_000_000 => $"{Downloads / 1_000_000.0:0.#} Mio. Downloads",
        >= 1_000 => $"{Downloads / 1_000.0:0.#} Tsd. Downloads",
        _ => $"{Downloads} Downloads"
    };

    private bool _installed;
    private bool _busy;

    public bool IsInstalled
    {
        get => _installed;
        set { _installed = value; Changed(); Changed(nameof(ActionText)); Changed(nameof(CanInstall)); }
    }

    public bool IsBusy
    {
        get => _busy;
        set { _busy = value; Changed(); Changed(nameof(ActionText)); Changed(nameof(CanInstall)); }
    }

    public string ActionText => IsBusy ? "Lädt..." : IsInstalled ? "Installiert" : "Installieren";
    public bool CanInstall => !IsBusy && !IsInstalled;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Eine konkrete Version (Datei) eines Projekts.</summary>
public class ContentVersion
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required ContentSource Source { get; init; }
    public required string Name { get; init; }
    public required string FileName { get; init; }
    public string? DownloadUrl { get; init; }
    public DateTime Date { get; init; }

    /// <summary>release, beta oder alpha</summary>
    public string Channel { get; init; } = "release";

    public IReadOnlyList<string> GameVersions { get; init; } = [];

    /// <summary>Kleingeschrieben, z.B. "fabric", "forge", "neoforge", "quilt".</summary>
    public IReadOnlyList<string> Loaders { get; init; } = [];

    public IReadOnlyList<string> RequiredProjectIds { get; init; } = [];
    public IReadOnlyList<string> IncompatibleProjectIds { get; init; } = [];

    public string ChannelText => Channel switch { "beta" => "Beta", "alpha" => "Alpha", _ => "Release" };
    public string Details => $"{ChannelText} · {Date:dd.MM.yyyy} · {FileName}";

    /// <summary>Läuft diese Version auf der Instanz (Minecraft-Version und bei Mods der Loader)?</summary>
    public bool Supports(Installation inst, ContentType type) =>
        GameVersions.Contains(inst.MinecraftVersion)
        && (type != ContentType.Mod || Loaders.Count == 0
            || Loaders.Contains(inst.Loader == LoaderType.Forge ? "forge" : "fabric"));
}

/// <summary>Eine Zeile in der Versionsauswahl.</summary>
public class VersionRow(ContentVersion version, bool isCurrent, bool isNewest) : INotifyPropertyChanged
{
    public ContentVersion Version { get; } = version;

    private bool _current = isCurrent;
    private bool _busy;

    public bool IsCurrent
    {
        get => _current;
        set { _current = value; Changed(); Changed(nameof(ActionText)); Changed(nameof(CanInstall)); Changed(nameof(Badge)); }
    }

    public bool IsBusy
    {
        get => _busy;
        set { _busy = value; Changed(); Changed(nameof(ActionText)); Changed(nameof(CanInstall)); }
    }

    public string Badge => IsCurrent ? "· installiert" : isNewest ? "· neueste" : "";
    public string ActionText => IsBusy ? "Lädt..." : IsCurrent ? "Installiert" : "Installieren";
    public bool CanInstall => !IsBusy && !IsCurrent;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Eine Seite Suchergebnisse plus Gesamtzahl der Treffer (für die Seitennavigation).</summary>
public record SearchPage(List<ContentProject> Items, int TotalHits);

public interface IContentProvider
{
    ContentSource Source { get; }

    /// <summary>Sucht eine Seite (0-basiert) mit <paramref name="pageSize"/> Einträgen.</summary>
    Task<SearchPage> SearchAsync(string query, ContentType type, Installation inst, int page, int pageSize);

    /// <summary>Alle zur Instanz passenden Versionen eines Projekts, neueste zuerst.</summary>
    Task<List<ContentVersion>> GetVersionsAsync(string projectId, ContentType type, Installation inst);

    /// <summary>Eine bestimmte Version (unabhängig davon, ob sie zur Instanz passt).</summary>
    Task<ContentVersion?> GetVersionAsync(string projectId, string versionId);

    Task<(string Id, string Title)> GetProjectInfoAsync(string idOrSlug);
}

public static class ContentProviderExtensions
{
    /// <summary>Neueste passende Version; Vollversionen werden gegenüber Beta/Alpha bevorzugt.</summary>
    public static async Task<ContentVersion?> GetLatestVersionAsync(this IContentProvider provider,
        string projectId, ContentType type, Installation inst)
    {
        var versions = await provider.GetVersionsAsync(projectId, type, inst);
        return versions.FirstOrDefault(v => v.Channel == "release") ?? versions.FirstOrDefault();
    }
}
