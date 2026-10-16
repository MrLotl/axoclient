using System.Text.RegularExpressions;
using AxoClient.Core.Common;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Launch.Checks;

/// <summary>RAM: mehr als der PC hat → Blocker; mehr als gerade frei oder zu wenig für die Mods → Warnung.</summary>
public sealed class MemoryCheck(InstanceManager instances, Func<(long TotalMb, long FreeMb)>? memory = null) : ILaunchCheck
{
    public string Id => "memory";
    public LaunchPhase Phase => LaunchPhase.Resources;

    public Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        var (total, free) = (memory ?? SystemInfo.Memory)();
        var mb = ctx.MemoryMb;
        var mods = ctx.Content.Mods.Count(m => m.Enabled);
        var recommended = SystemInfo.RecommendedMemoryMb(mods, total);
        var setRecommended = new CheckAction("setRecommended", TextRef.Of("pipeline.action.setRam", ("mb", recommended)),
            async (c, job) =>
            {
                c.Instance = await instances.UpdateAsync(c.Instance.Id, new InstanceUpdate(MemoryMb: recommended), job.Token);
            }, true);

        var results = new List<CheckResult>();
        if (mb > total - 512)
            results.Add(new CheckResult(Id, Severity.Blocker, TextRef.Of("pipeline.ram.tooHigh", ("mb", mb), ("totalMb", total)),
                [setRecommended], CanSilence: false));
        else
        {
            if (mb > free)
                results.Add(new CheckResult(Id, Severity.Warning, TextRef.Of("pipeline.ram.notFree", ("mb", mb), ("freeMb", free)),
                    [
                        new CheckAction("lessRam", TextRef.Of("pipeline.action.lessRam", ("mb", Math.Max(1024, free / 512 * 512))),
                            async (c, job) => c.Instance = await instances.UpdateAsync(c.Instance.Id,
                                new InstanceUpdate(MemoryMb: (int)Math.Max(1024, free / 512 * 512)), job.Token))
                    ], Key: "ram-not-free"));
            if (mb < SystemInfo.MinimumMemoryMb(mods))
                results.Add(new CheckResult(Id, Severity.Warning, TextRef.Of("pipeline.ram.tooLow", ("mb", mb), ("mods", mods)),
                    [setRecommended], Key: $"ram-low:{mods / 25}"));
        }
        return Task.FromResult<IReadOnlyList<CheckResult>>(results.Count == 0 ? [CheckResult.Ok(Id)] : results);
    }
}

/// <summary>Java-Argumente: doppeltes -Xmx/-Xms → Auto-Fix (RAM-Regler hat Vorrang); ungültig → Blocker.</summary>
public sealed partial class JavaArgsCheck(InstanceManager instances) : ILaunchCheck
{
    public string Id => "javaArgs";
    public LaunchPhase Phase => LaunchPhase.Resources;

    public Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        var args = ctx.Instance.JavaArgs;
        if (string.IsNullOrWhiteSpace(args))
            return Task.FromResult<IReadOnlyList<CheckResult>>([CheckResult.Ok(Id)]);

        if (!IsValid(args))
            return Task.FromResult<IReadOnlyList<CheckResult>>([new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.javaArgs.invalid"),
                [new CheckAction("reset", new TextRef("pipeline.action.resetArgs"), async (c, job) =>
                    c.Instance = await instances.UpdateAsync(c.Instance.Id, new InstanceUpdate(JavaArgs: ""), job.Token), true)],
                CanSilence: false)]);

        if (MemoryArg().IsMatch(args))
            return Task.FromResult<IReadOnlyList<CheckResult>>([new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.javaArgs.memory"),
                [new CheckAction("strip", new TextRef("pipeline.action.preferSlider"), async (c, job) =>
                    c.Instance = await instances.UpdateAsync(c.Instance.Id, new InstanceUpdate(JavaArgs: StripMemory(c.Instance.JavaArgs)),
                        job.Token), true)])]);

        return Task.FromResult<IReadOnlyList<CheckResult>>([CheckResult.Ok(Id)]);
    }

    public static string StripMemory(string args) =>
        string.Join(' ', Split(args).Where(a => !MemoryArg().IsMatch(a)));

    /// <summary>Jedes Argument muss mit - beginnen, Anführungszeichen müssen paarig sein, keine Steuerzeichen.</summary>
    public static bool IsValid(string args)
    {
        if (args.Count(c => c == '"') % 2 != 0 || args.Any(char.IsControl))
            return false;
        var parts = Split(args);
        return parts.Count > 0 && parts.All(p => p.StartsWith('-') && p.Length > 1)
                               && !parts.Any(p => p.StartsWith("-jar", StringComparison.Ordinal) || p is "-cp" or "-classpath");
    }

    public static List<string> Split(string args)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in args)
        {
            if (c == '"')
                quoted = !quoted;
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
                current.Append(c);
        }
        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }

    [GeneratedRegex(@"(^|\s)-Xm[xs]\d+[kKmMgG]?\b")]
    private static partial Regex MemoryArg();
}
