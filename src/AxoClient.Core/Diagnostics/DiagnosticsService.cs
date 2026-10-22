using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using AxoClient.Core.Accounts;
using AxoClient.Core.Common;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;
using AxoClient.Core.Launch;
using AxoClient.Core.Social;
using Microsoft.Win32;

namespace AxoClient.Core.Diagnostics;

public enum DiagnosticStatus
{
    Ok,
    Warning,
    Error
}

/// <summary>Eine Zeile im Diagnose-Ergebnis, jeweils mit Lösung.</summary>
public sealed record DiagnosticItem(string Area, DiagnosticStatus Status, TextRef Title, TextRef? Detail = null, TextRef? Solution = null);

/// <summary>Eine Prüfung der eingebauten Diagnose.</summary>
public interface IDiagnosticCheck
{
    string Area { get; }
    Task<IReadOnlyList<DiagnosticItem>> RunAsync(CancellationToken ct);
}

/// <summary>"Diagnose starten" in den Einstellungen: der Launcher prüft sich selbst.</summary>
public sealed class DiagnosticsService(IEnumerable<IDiagnosticCheck> checks)
{
    public async Task<IReadOnlyList<DiagnosticItem>> RunAsync(JobContext job)
    {
        var list = checks.ToList();
        var results = new List<DiagnosticItem>();
        for (var i = 0; i < list.Count; i++)
        {
            job.Report((double)i / list.Count, TextRef.Of("diagnostics.running", ("area", list[i].Area)));
            try
            {
                results.AddRange(await list[i].RunAsync(job.Token));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new DiagnosticItem(list[i].Area, DiagnosticStatus.Error, new TextRef("diagnostics.checkFailed"),
                    AxoException.Describe(ex)));
            }
        }
        job.Report(1);
        return results;
    }

    /// <summary>"Bericht kopieren": lesbar und geschwärzt (Tokens, Spielername, UUID, IP-Adressen).</summary>
    public static string Report(IReadOnlyList<DiagnosticItem> items, Func<TextRef, string> translate)
    {
        var text = new StringBuilder()
            .AppendLine($"AxoClient {AppInfo.Version} · Diagnose {DateTime.Now:yyyy-MM-dd HH:mm}")
            .AppendLine(SystemInfo.WindowsVersion)
            .AppendLine();
        foreach (var group in items.GroupBy(i => i.Area))
        {
            text.AppendLine($"[{group.Key}]");
            foreach (var item in group)
            {
                text.Append(item.Status switch { DiagnosticStatus.Ok => "  OK      ", DiagnosticStatus.Warning => "  WARNUNG ", _ => "  FEHLER  " })
                    .Append(translate(item.Title));
                if (item.Detail != null)
                    text.Append(" – ").Append(translate(item.Detail));
                text.AppendLine();
            }
        }
        return LogRedactor.RedactForSharing(text.ToString());
    }
}

public sealed class SystemDiagnostic(AppPaths paths) : IDiagnosticCheck
{
    public string Area => "system";

    public Task<IReadOnlyList<DiagnosticItem>> RunAsync(CancellationToken ct)
    {
        var (total, free) = SystemInfo.Memory();
        var disk = SystemInfo.FreeDiskBytes(paths.Root);
        var items = new List<DiagnosticItem>
        {
            new(Area, DiagnosticStatus.Ok, TextRef.Of("diagnostics.windows", ("version", Environment.OSVersion.Version.ToString()))),
            new(Area, total < 4096 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                TextRef.Of("diagnostics.ram", ("totalMb", total), ("freeMb", free)),
                Solution: total < 4096 ? new TextRef("diagnostics.ram.solution") : null),
            new(Area, disk < 5L * 1024 * 1024 * 1024 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                TextRef.Of("diagnostics.disk", ("freeGb", disk / 1024 / 1024 / 1024)),
                Solution: disk < 5L * 1024 * 1024 * 1024 ? new TextRef("diagnostics.disk.solution") : null)
        };
        foreach (var (name, version) in GraphicsCards())
            items.Add(new DiagnosticItem(Area, DiagnosticStatus.Ok, TextRef.Of("diagnostics.gpu", ("name", name), ("driver", version))));
        return Task.FromResult<IReadOnlyList<DiagnosticItem>>(items);
    }

    /// <summary>Grafikkarten und Treiberversion aus der Registry (ohne WMI).</summary>
    internal static IEnumerable<(string Name, string Driver)> GraphicsCards()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (key == null)
            yield break;
        foreach (var sub in key.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            using var card = key.OpenSubKey(sub);
            if (card?.GetValue("DriverDesc") is string name && card.GetValue("DriverVersion") is string version)
                yield return (Text.Clean(name, 80), Text.Clean(version, 40));
        }
    }
}

public sealed class PathDiagnostic(AppPaths paths) : IDiagnosticCheck
{
    public string Area => "paths";

    public Task<IReadOnlyList<DiagnosticItem>> RunAsync(CancellationToken ct)
    {
        var items = new List<DiagnosticItem>();
        try
        {
            var probe = Path.Combine(paths.Tmp, $"probe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(paths.Tmp);
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            items.Add(new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.writable")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            items.Add(new(Area, DiagnosticStatus.Error, new TextRef("diagnostics.notWritable"), Solution: new TextRef("diagnostics.notWritable.solution")));
        }
        if (paths.Root.Any(c => c > 127))
            items.Add(new(Area, DiagnosticStatus.Warning, new TextRef("diagnostics.pathUmlauts"), Solution: new TextRef("diagnostics.pathUmlauts.solution")));
        var longPaths = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem")?.GetValue("LongPathsEnabled") is 1;
        items.Add(new(Area, longPaths ? DiagnosticStatus.Ok : DiagnosticStatus.Warning, new TextRef(longPaths ? "diagnostics.longPaths" : "diagnostics.noLongPaths"),
            Solution: longPaths ? null : new TextRef("diagnostics.noLongPaths.solution")));
        return Task.FromResult<IReadOnlyList<DiagnosticItem>>(items);
    }
}

public sealed class JavaDiagnostic(JavaManager java) : IDiagnosticCheck
{
    public string Area => "java";

    public async Task<IReadOnlyList<DiagnosticItem>> RunAsync(CancellationToken ct)
    {
        var runtimes = await java.FindAllAsync(ct);
        if (runtimes.Count == 0)
            return [new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.javaNone"))];
        return runtimes.Select(r => r.Major > 0
            ? new DiagnosticItem(Area, DiagnosticStatus.Ok, TextRef.Of("diagnostics.javaOk", ("major", r.Major), ("source", r.Source)))
            : new DiagnosticItem(Area, DiagnosticStatus.Error, TextRef.Of("diagnostics.javaBroken", ("source", r.Source)),
                Solution: new TextRef("diagnostics.javaBroken.solution"))).ToList();
    }
}

public sealed class NetworkDiagnostic(ApiClient api) : IDiagnosticCheck
{
    private static readonly (string Name, string Url)[] Targets =
    [
        ("Mojang", "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"),
        ("Microsoft", "https://login.live.com/"),
        ("Modrinth", "https://api.modrinth.com/v2/tag/loader"),
        ("AxoClient", AppInfo.ServiceUrl + "/v2/health"),
        ("GitHub", "https://api.github.com/")
    ];

    public string Area => "network";

    public async Task<IReadOnlyList<DiagnosticItem>> RunAsync(CancellationToken ct)
    {
        var tasks = Targets.Select(async t =>
        {
            var watch = Stopwatch.StartNew();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var response = await api.Http.GetAsync(t.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                return new DiagnosticItem(Area, (int)response.StatusCode < 500 ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
                    TextRef.Of("diagnostics.reachable", ("name", t.Name), ("ms", watch.ElapsedMilliseconds)));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                return new DiagnosticItem(Area, DiagnosticStatus.Error, TextRef.Of("diagnostics.unreachable", ("name", t.Name)),
                    Solution: new TextRef("diagnostics.unreachable.solution"));
            }
        });
        return await Task.WhenAll(tasks);
    }
}

public sealed class LauncherFilesDiagnostic(AppPaths paths) : IDiagnosticCheck
{
    public string Area => "launcher";

    public Task<IReadOnlyList<DiagnosticItem>> RunAsync(CancellationToken ct)
    {
        var items = new List<DiagnosticItem>();
        var jsonFiles = new[] { paths.LauncherJson, paths.AccountsJson, paths.StateJson, paths.SkinLibrary, paths.CapeLibrary }
            .Concat(Directory.Exists(paths.Instances)
                ? Directory.EnumerateDirectories(paths.Instances).SelectMany(d => new[]
                    { Path.Combine(d, "instance.json"), Path.Combine(d, "content.json"), Path.Combine(d, "playtime.json") })
                : [])
            .Concat(Directory.Exists(paths.Servers)
                ? Directory.EnumerateDirectories(paths.Servers).SelectMany(d => new[] { Path.Combine(d, "server.json"), Path.Combine(d, "uptime.json") })
                : [])
            .Where(File.Exists)
            .ToList();
        var broken = new List<string>();
        foreach (var file in jsonFiles)
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
                if (node?["formatVersion"]?.GetValue<int>() is not (>= 1 and <= 1))
                    broken.Add(Path.GetRelativePath(paths.Root, file));
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or InvalidOperationException or FormatException)
            {
                broken.Add(Path.GetRelativePath(paths.Root, file));
            }
        }
        items.Add(broken.Count == 0
            ? new(Area, DiagnosticStatus.Ok, TextRef.Of("diagnostics.jsonOk", ("count", jsonFiles.Count)))
            : new(Area, DiagnosticStatus.Error, TextRef.Of("diagnostics.jsonBroken", ("files", string.Join(", ", broken.Take(5)))),
                Solution: new TextRef("diagnostics.jsonBroken.solution")));

        var leftovers = Directory.EnumerateFiles(paths.Root, "*.tmp", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
            .Count();
        if (leftovers > 0)
            items.Add(new(Area, DiagnosticStatus.Warning, TextRef.Of("diagnostics.tmpLeftovers", ("count", leftovers)),
                Solution: new TextRef("diagnostics.tmpLeftovers.solution")));
        return Task.FromResult<IReadOnlyList<DiagnosticItem>>(items);
    }
}

public sealed class AccountDiagnostic(AccountManager accounts, OnlineClient online) : IDiagnosticCheck
{
    public string Area => "account";

    public async Task<IReadOnlyList<DiagnosticItem>> RunAsync(CancellationToken ct)
    {
        if (accounts.Active == null)
            return [new(Area, DiagnosticStatus.Warning, new TextRef("diagnostics.noAccount"), Solution: new TextRef("diagnostics.noAccount.solution"))];
        var items = new List<DiagnosticItem>();
        try
        {
            await accounts.GetSessionAsync(ct, forceRefresh: true);
            items.Add(new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.loginOk")));
        }
        catch (AxoException ex)
        {
            items.Add(new(Area, DiagnosticStatus.Error, new TextRef("diagnostics.loginBroken"), ex.Text, new TextRef("diagnostics.loginBroken.solution")));
            return items;
        }
        try
        {
            await online.SendAsync(HttpMethod.Get, "/me", ct: ct);
            items.Add(new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.serviceOk")));
        }
        catch (AxoException ex)
        {
            items.Add(new(Area, DiagnosticStatus.Warning, new TextRef("diagnostics.serviceBroken"), ex.Text));
        }
        return items;
    }
}

/// <summary>Alle Prüfungen der Start-Pipeline für jede Instanz, ohne zu starten.</summary>
public sealed class InstancesDiagnostic(InstanceManager instances, LaunchPipeline pipeline) : IDiagnosticCheck
{
    public string Area => "instances";

    public async Task<IReadOnlyList<DiagnosticItem>> RunAsync(CancellationToken ct)
    {
        var items = new List<DiagnosticItem>();
        foreach (var instance in await instances.LoadAllAsync(ct))
        {
            var results = await pipeline.DiagnoseAsync(instance.Id, ct);
            var problems = results.Where(r => r.Severity is Severity.Warning or Severity.Blocker).ToList();
            if (problems.Count == 0)
                items.Add(new(Area, DiagnosticStatus.Ok, TextRef.Of("diagnostics.instanceOk", ("name", instance.Name))));
            foreach (var p in problems)
                items.Add(new(Area, p.Severity == Severity.Blocker ? DiagnosticStatus.Error : DiagnosticStatus.Warning,
                    TextRef.Of("diagnostics.instanceIssue", ("name", instance.Name)), p.Message, p.Actions.FirstOrDefault()?.Label));
        }
        return items;
    }
}
