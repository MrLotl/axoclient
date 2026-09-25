using System.Text.RegularExpressions;

namespace AxoClient.Content;

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
    public static readonly ContentType[] All = [ContentType.Mod, ContentType.ResourcePack, ContentType.Shader];

    public static string Folder(ContentType type, string mcVersion) => type switch
    {
        ContentType.Mod => "mods",
        ContentType.ResourcePack => UsesTexturePacks(mcVersion) ? "texturepacks" : "resourcepacks",
        _ => "shaderpacks"
    };

    public static string Label(ContentType type) => type switch
    {
        ContentType.Mod => "Mod",
        ContentType.ResourcePack => "Ressourcenpaket",
        _ => "Shader"
    };

    public static bool UsesTexturePacks(string version)
    {
        if (version.StartsWith('a') || version.StartsWith('b') || version.StartsWith('c')
            || version.StartsWith("rd-") || version.StartsWith("inf-"))
            return true;

        var release = Regex.Match(version, @"^1\.(\d+)");
        if (release.Success)
            return int.Parse(release.Groups[1].Value) < 6;

        var snapshot = Regex.Match(version, @"^(\d\d)w(\d\d)");
        if (snapshot.Success)
        {
            int year = int.Parse(snapshot.Groups[1].Value), week = int.Parse(snapshot.Groups[2].Value);
            return year < 13 || (year == 13 && week < 24);
        }
        return false;
    }
}

public class ContentProject : Observable
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string? IconUrl { get; init; }
    public long Downloads { get; init; }
    public string? WebsiteUrl { get; init; }
    public string Tags { get; init; } = "";

    public string TagsSuffix => Tags.Length > 0 ? "  ·  " + Tags : "";

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
        set { _installed = value; Changed(nameof(IsInstalled), nameof(ActionText), nameof(CanInstall)); }
    }

    public bool IsBusy
    {
        get => _busy;
        set { _busy = value; Changed(nameof(IsBusy), nameof(ActionText), nameof(CanInstall)); }
    }

    public string ActionText => IsBusy ? "Lädt..." : IsInstalled ? "Installiert" : "Installieren";
    public bool CanInstall => !IsBusy && !IsInstalled;
}

public class ContentVersion
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string Name { get; init; }
    public required string FileName { get; init; }
    public string? DownloadUrl { get; init; }
    public DateTime Date { get; init; }
    public long Size { get; init; }
    public string Channel { get; init; } = "release";
    public IReadOnlyList<string> GameVersions { get; init; } = [];
    public IReadOnlyList<string> Loaders { get; init; } = [];
    public IReadOnlyList<string> RequiredProjectIds { get; init; } = [];
    public IReadOnlyList<string> IncompatibleProjectIds { get; init; } = [];

    public bool IsRelease => Channel == "release";
    public string ChannelText => Channel switch { "beta" => "Beta", "alpha" => "Alpha", _ => "Release" };
    public string Details => $"{ChannelText} · {Date:dd.MM.yyyy} · {FileName}";

    public bool Supports(Installation inst, ContentType type) =>
        GameVersions.Contains(inst.MinecraftVersion)
        && (type != ContentType.Mod || Loaders.Count == 0 || Loaders.Contains(inst.Loader.ModrinthName()));

    public static ContentVersion? Newest(IReadOnlyList<ContentVersion> versions) =>
        versions.FirstOrDefault(v => v.IsRelease) ?? versions.FirstOrDefault();
}

public class VersionRow(ContentVersion version, bool isCurrent, bool isNewest) : Observable
{
    public ContentVersion Version { get; } = version;

    private bool _current = isCurrent;
    private bool _busy;

    public bool IsCurrent
    {
        get => _current;
        set { _current = value; Changed(nameof(IsCurrent), nameof(ActionText), nameof(CanInstall), nameof(Badge)); }
    }

    public bool IsBusy
    {
        get => _busy;
        set { _busy = value; Changed(nameof(IsBusy), nameof(ActionText), nameof(CanInstall)); }
    }

    public string Badge => IsCurrent ? "· installiert" : isNewest ? "· neueste" : "";
    public string ActionText => IsBusy ? "Lädt..." : IsCurrent ? "Installiert" : "Installieren";
    public bool CanInstall => !IsBusy && !IsCurrent;
}

public record SearchPage(List<ContentProject> Items, int TotalHits);

public record ProjectSummary(string Id, string Title, string? IconUrl);
