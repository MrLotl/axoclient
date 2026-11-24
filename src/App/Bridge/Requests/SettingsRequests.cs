using AxoClient.DataModel.Settings;

namespace AxoClient.App.Bridge.Requests
{
    public sealed record SettingsData(LauncherSettings Settings);

    public sealed record StateData(string? Instances, string? LocalServers, string? Modpacks, string? SortModpacks, string? SortAddMods,
            string? Content = null, double? ContentGridSize = null, double? ContentListSize = null,
            double? InstancesSize = null, string? SortInstances = null, string? Favorite = null, string[]? Order = null);

    public sealed record AccentData(string From, string To, int Angle);

    public sealed record CleanData(string Area);

    public sealed record JavaRefData(string FileRef);
}
