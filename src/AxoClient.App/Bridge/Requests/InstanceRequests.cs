using AxoClient.DataModel.Game;

namespace AxoClient.App.Bridge.Requests
{
    public sealed record IdData(string Id);

    public sealed record RepairOneData(string Id, string IssueId, string? ActionId);

    public sealed record VersionListData(bool Snapshots = false, bool Old = false);

    public sealed record LoaderVersionsData(LoaderType Loader, string Minecraft);

    public sealed record CreateData(string Name, string Minecraft, LoaderType Loader, string? LoaderVersion, int? MemoryMb, string? IconRef, string? ArtSeed = null);

    public sealed record UpdateData(string Id, string? Name, string? Minecraft, LoaderType? Loader, string? LoaderVersion, int? MemoryMb,
            bool ResetMemory, string? JavaArgs, bool? BannerAuto, bool? AxoMod);

    public sealed record ImageData(string Id, bool Banner, string? FileRef, string? Base64);

    public sealed record BackdropData(string Id, bool GradientOnly, int PanoramaView);

    public sealed record CopyData(string Id, string Name);

    public sealed record ExportData(string Id, string Format, string FileRef);

    public sealed record ImportData(string FileRef);

    public sealed record WorldData(string Id, string Folder);

    public sealed record WorldImportData(string Id, List<string> FileRefs);

    public sealed record BackupFileData(string Id, string File);

    public sealed record DatapackInstallData(string InstanceId, string World, string Source, string ProjectId, string? VersionId);

    public sealed record DatapackFileData(string Id, string World, string File, bool Enabled = true);

    public sealed record ServerAddData(string Id, string Name, string Address, int? Index);

    public sealed record ServerEditData(string Id, int Index, string Name, string Address);

    public sealed record ServerIndexData(string Id, int Index);

    public sealed record ServerMoveData(string Id, int From, int To);

    public sealed record AddressData(string Address);

    public sealed record RenameData(string Id, string File, string Name);
}
