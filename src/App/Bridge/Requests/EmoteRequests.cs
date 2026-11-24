namespace AxoClient.App.Bridge.Requests
{
    public sealed record EmoteImportData(List<string> FileRefs);

    public sealed record EmoteRenameData(string Id, string Name);

    public sealed record EmoteSlotsData(List<string?> Slots);

    public sealed record EmoteOptionsData(string Id, bool Loop, bool Moving);

    public sealed record MissingModData(string Source, string ProjectId, string? VersionId);

    public sealed record InstallMissingData(string InstanceId, string Address, List<MissingModData> Mods);
}
