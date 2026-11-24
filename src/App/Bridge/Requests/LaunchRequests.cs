using AxoClient.DataModel.Launch;

namespace AxoClient.App.Bridge.Requests
{
    public sealed record LaunchData(string Id, string? World, string? Server);

    public sealed record PromptIdData(string PromptId);

    public sealed record ResolveData(string PromptId, PromptChoice Choice, Dictionary<string, string>? Actions, List<string>? Silence);

    public sealed record CrashActionData(string Id, string Action, string? File);

    public sealed record ConsoleData(string Key, int Max = 5000);

    public sealed record CommandData(string Id, string Command);
}
