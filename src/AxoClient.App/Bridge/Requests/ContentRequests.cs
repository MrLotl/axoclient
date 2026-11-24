using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;

namespace AxoClient.App.Bridge.Requests
{
    public sealed record ContentListData(string Id, ContentType Type);

    public sealed record SearchData(string Source, ProjectKind Kind, string Text, string? Minecraft, LoaderType? Loader, string? Category,
            SearchSort Sort, int Page, int PageSize = 20);

    public sealed record ProjectData(string Source, string Id);

    public sealed record ProjectVersionsData(string Source, string ProjectId, string? InstanceId, ProjectKind Kind, bool AllVersions = false);

    public sealed record InstallData(string InstanceId, ContentType Type, string Source, string ProjectId, string? VersionId);

    public sealed record EnableData(string Id, ContentType Type, string File, bool Enabled);

    public sealed record RemoveData(string Id, ContentType Type, List<string> Files);

    public sealed record RollbackData(string Id, ContentType Type, string File);

    public sealed record AddFilesData(string Id, ContentType Type, List<string> FileRefs);

    public sealed record VersionsForData(string Id, ContentType Type, string File, bool All = false);

    public sealed record ApplyUpdatesData(string Id, List<ContentUpdate> Updates);

    public sealed record ModpackInstallData(string Source, string ProjectId, string? VersionId);

    public sealed record ModpackUpdateData(string Id, string VersionId);
}
