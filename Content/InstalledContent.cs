using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace AxoClient.Content;

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

    [JsonIgnore] public bool IsFromModrinth => Source == ContentSource.Modrinth;

    public static InstalledContent Create(ContentType type, string title, ContentVersion version, string? fileName = null) => new()
    {
        FileName = fileName ?? version.FileName,
        Type = type,
        Source = ContentSource.Modrinth,
        ProjectId = version.ProjectId,
        Title = title,
        VersionId = version.Id,
        VersionName = version.Name,
        VersionDate = version.Date
    };
}

public class InstalledItem : Observable
{
    public required string FullPath { get; init; }
    public required string DisplayName { get; init; }
    public required string FileName { get; init; }
    public required bool Enabled { get; init; }
    public required bool CanToggle { get; init; }
    public required ContentType Type { get; init; }
    public InstalledContent? Entry { get; init; }

    private BitmapSource? _icon;

    public BitmapSource? Icon
    {
        get => _icon;
        set { _icon = value; Changed(); }
    }

    public bool CanChangeVersion => Entry != null;
    public bool CanShare => Entry is { IsFromModrinth: true };

    public string SourceText => Entry == null
        ? $"Manuell · {FileName}"
        : $"{Entry.Source} · {Entry.VersionName ?? FileName}";

    private ContentVersion? _update;

    public ContentVersion? Update
    {
        get => _update;
        set { _update = value; Changed(nameof(Update), nameof(HasUpdate), nameof(UpdateText)); }
    }

    public bool HasUpdate => Update != null;
    public string UpdateText => Update == null ? "" : $"Update: {Update.Name}";
}
