using AxoClient.Core.Common;
using AxoClient.Core.Instances;
using AxoClient.Core.Launch;
using AxoClient.Core.Servers;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record LaunchData(string Id, string? World, string? Server);

    private sealed record PromptIdData(string PromptId);

    private sealed record ResolveData(string PromptId, PromptChoice Choice, Dictionary<string, string>? Actions, List<string>? Silence);

    private sealed record CrashActionData(string Id, string Action, string? File);

    private sealed record ConsoleData(string Key, int Max = 5000);

    private sealed record CommandData(string Id, string Command);

    private static void RegisterLaunch(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;

        r.On<LaunchData>("instances.launch", async (d, _) =>
        {
            Text.RequireId(d.Id);
            var quickPlay = d.World != null || d.Server != null ? new QuickPlay(d.World, d.Server) : null;
            if (d.Server != null && !ServerPing.IsValidAddress(d.Server))
                throw new AxoException("servers.addressInvalid");
            await host.Settings.UpdateStateAsync(s => s.LastInstance = d.Id);
            return StartJob(host.Games.LaunchAsync(d.Id, quickPlay));
        });

        r.On<IdData>("instances.stop", async (d, _) =>
        {
            await host.Games.StopAsync(d.Id);
            return null;
        });

        r.On("games.running", () => host.Games.Running);

        r.On<PromptIdData>("launch.promptShown", (d, _) => Task.FromResult<object?>(host.Prompt.Shown(d.PromptId)));
        r.On<ResolveData>("launch.resolve", (d, _) => Task.FromResult<object?>(host.Prompt.Resolve(d.PromptId,
            new PromptDecision(d.Choice, d.Actions, d.Silence))));

        r.On<CrashActionData>("crash.action", async (d, _) =>
        {
            await host.Jobs.RunAsync("crash.action", new TextRef("crash.fixing"), job => host.CrashActions.RunAsync(d.Id, d.Action, d.File, job), d.Id);
            return null;
        });

        // ---- Live-Konsole ----
        r.On("console.sources", () => host.Processes.All.Select(p => new
        {
            key = p.Key,
            kind = p.Key.StartsWith("server:") ? "server" : "instance",
            id = p.Key[(p.Key.IndexOf(':') + 1)..],
            running = !p.HasExited,
            startedAt = p.StartedAt
        }).ToList());

        r.On<ConsoleData>("console.lines", (d, _) => Task.FromResult<object?>(
            host.Processes.GetIncludingExited(d.Key)?.Lines(Math.Clamp(d.Max, 1, 20_000)) ?? []));

        r.On<ConsoleData>("console.copy", (d, _) => Task.FromResult<object?>(LogRedactor.RedactForSharing(
            string.Join('\n', (host.Processes.GetIncludingExited(d.Key)?.Lines(Math.Clamp(d.Max, 1, 20_000)) ?? []).Select(l => l.Text)))));

        r.On<CommandData>("console.command", async (d, _) =>
        {
            await host.LocalServers.SendCommandAsync(d.Id, d.Command);
            return null;
        });
    }
}
