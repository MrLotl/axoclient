using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Core.Common;
using AxoClient.Core.Content;

namespace AxoClient.Core.Launch;

public sealed class CrashRule
{
    public string Id { get; set; } = "";
    public List<string> Match { get; set; } = [];

    /// <summary><c>crash</c>, <c>hserr</c>, <c>log</c>; leer = überall.</summary>
    public List<string> In { get; set; } = [];
    public string Cause { get; set; } = "";
    public List<string> Actions { get; set; } = [];

    /// <summary><c>stacktrace</c>, <c>mixin</c> oder <c>fabricResolution</c>.</summary>
    public string? FindMod { get; set; }
    public bool Server { get; set; }
}

public sealed class CrashRuleSet
{
    public int FormatVersion { get; set; } = 1;
    public List<CrashRule> Rules { get; set; } = [];
}

/// <summary>Material für die Analyse: Exit-Code, Crash-Report bzw. hs_err und die letzten Log-Zeilen.</summary>
public sealed record CrashInput(int ExitCode, string? CrashReport, string? HsErr, string LogTail, string? CrashReportFile);

public sealed record CrashSuspect(string File, string Name);

public sealed record CrashAnalysis(
    bool KnownCause,
    TextRef Cause,
    string? RuleId,
    IReadOnlyList<string> Actions,
    IReadOnlyList<CrashSuspect> Suspects,
    IReadOnlyList<CrashSuspect> RecentlyAdded,
    string? ConfigFile,
    string? CrashReportFile,
    int ExitCode);

/// <summary>
/// Sucht per Regeln aus <c>crash-rules.json</c> die wahrscheinliche Ursache. Eine Ursache wird nur genannt, wenn eine
/// Regel sicher passt; sonst "Ursache unbekannt" mit den zuletzt hinzugefügten Mods als mögliche Auslöser.
/// </summary>
public sealed partial class CrashAnalyzer
{
    private const int MaxText = 600_000;
    private readonly IReadOnlyList<(CrashRule Rule, Regex[] Patterns)> _rules;

    public CrashAnalyzer() : this(LoadEmbeddedRules())
    {
    }

    public CrashAnalyzer(CrashRuleSet rules)
    {
        _rules = rules.Rules.Select(r => (r, r.Match.Select(m =>
            new Regex(m, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1))).ToArray())).ToList();
    }

    public static CrashRuleSet LoadEmbeddedRules()
    {
        using var stream = typeof(CrashAnalyzer).Assembly.GetManifestResourceStream("crash-rules.json")
                           ?? throw new InvalidOperationException("crash-rules.json fehlt");
        return JsonSerializer.Deserialize<CrashRuleSet>(stream, Json.Options)!;
    }

    public IReadOnlyList<CrashRule> Rules => _rules.Select(r => r.Rule).ToList();

    public CrashAnalysis Analyze(CrashInput input, ContentDocument? content, bool server = false)
    {
        var sources = new Dictionary<string, string>
        {
            ["crash"] = Trim(input.CrashReport),
            ["hserr"] = Trim(input.HsErr),
            ["log"] = Trim(input.LogTail)
        };
        // Nur aktive Mods: deaktivierte können den Absturz nicht ausgelöst haben
        var mods = content?.Mods.Where(m => m.Enabled && m.Source != ContentSource.AxoClient).ToList() ?? [];

        foreach (var (rule, patterns) in _rules.Where(r => r.Rule.Server == server))
        {
            var where = rule.In.Count > 0 ? rule.In : ["crash", "hserr", "log"];
            foreach (var source in where)
            {
                var text = sources.GetValueOrDefault(source, "");
                if (text.Length == 0)
                    continue;
                Match? match = null;
                try
                {
                    match = patterns.Select(p => p.Match(text)).FirstOrDefault(m => m.Success);
                }
                catch (RegexMatchTimeoutException)
                {
                }
                if (match == null)
                    continue;

                var allText = string.Join('\n', sources.Values);
                var suspects = rule.FindMod switch
                {
                    "mixin" => FindByMixin(allText, mods),
                    "fabricResolution" => FindByResolution(allText, mods),
                    "stacktrace" => FindByStacktrace(allText, mods),
                    _ => []
                };
                // Forge/NeoForge nennen verdächtige Mods selbst
                if (suspects.Count == 0)
                    suspects = FindForgeSuspects(sources["crash"], mods);
                var file = match.Groups["file"] is { Success: true } g ? g.Value : null;
                // Zwei Mods beißen sich: beide nennen, statt nur "verträgt sich nicht"
                var cause = rule.FindMod == "mixin" && suspects.Count >= 2
                    ? TextRef.Of("crash.cause.mixinConflict", ("mod", suspects[0].Name), ("other", suspects[1].Name))
                    : TextRef.Of(rule.Cause, ("file", file), ("mod", suspects.FirstOrDefault()?.Name));
                return new CrashAnalysis(true, cause,
                    rule.Id, rule.Actions, suspects, [], file, input.CrashReportFile, input.ExitCode);
            }
        }

        var recent = mods.OrderByDescending(m => m.AddedAt).Take(3).Select(m => new CrashSuspect(m.File, m.Name)).ToList();
        var stack = FindByStacktrace(string.Join('\n', sources.Values), mods);
        return new CrashAnalysis(false, new TextRef("crash.cause.unknown"), null, mods.Count > 0 ? ["checkMods"] : [], stack, recent,
            null, input.CrashReportFile, input.ExitCode);
    }

    /// <summary>Paketnamen im Stacktrace den Mods zuordnen (Pakete merkt sich der Launcher beim Installieren).</summary>
    internal static List<CrashSuspect> FindByStacktrace(string text, List<ContentEntry> mods)
    {
        var frames = Regex.Matches(text, @"at ([a-zA-Z_][\w$]*(?:\.[a-zA-Z_][\w$]*){2,})\.").Select(m => m.Groups[1].Value)
            .Where(f => !f.StartsWith("java.") && !f.StartsWith("jdk.") && !f.StartsWith("sun.") && !f.StartsWith("net.minecraft.")
                        && !f.StartsWith("com.mojang.") && !f.StartsWith("net.fabricmc.loader") && !f.StartsWith("org.spongepowered")
                        && !f.StartsWith("cpw.mods") && !f.StartsWith("net.neoforged.fml") && !f.StartsWith("net.minecraftforge.fml"))
            .Take(60)
            .ToList();
        var hits = new List<CrashSuspect>();
        foreach (var frame in frames)
        {
            var mod = mods.FirstOrDefault(m => m.Meta?.Packages.Any(p => frame.StartsWith(p + ".", StringComparison.Ordinal)) == true);
            if (mod != null && hits.All(h => h.File != mod.File))
                hits.Add(new CrashSuspect(mod.File, mod.Name));
            if (hits.Count >= 3)
                break;
        }
        return hits;
    }

    /// <summary>
    /// Mixin-Fehler: nur die eigentlichen Fehlerzeilen auswerten. Beim normalen Laden nennt das Log die Mixin-Konfiguration
    /// jeder Mod; wer das ganze Log durchsucht, verdächtigt am Ende alle. Reihenfolge: die Mod, deren Mixin scheitert,
    /// danach die Mod(s), mit denen sie sich beißt ("merged by", "already redirected by", "@Redirect conflict").
    /// </summary>
    internal static List<CrashSuspect> FindByMixin(string text, List<ContentEntry> mods)
    {
        var errorLines = text.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("at ", StringComparison.Ordinal))
            .Where(l => MixinErrorLine().IsMatch(l))
            .ToList();
        var primary = new List<string>();
        var others = new List<string>();
        foreach (var line in errorLines)
        {
            foreach (Match m in Regex.Matches(line, @"Mixin apply for mod ([a-z0-9_]+(?:-[a-z0-9_]+)*) failed"))
                primary.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(line, @"from mod ([a-z0-9_]+(?:-[a-z0-9_]+)*)"))
                others.Add(m.Groups[1].Value);
        }

        var hits = new List<CrashSuspect>();
        void Add(ContentEntry? mod)
        {
            if (mod != null && hits.All(h => h.File != mod.File))
                hits.Add(new CrashSuspect(mod.File, mod.Name));
        }
        ContentEntry? ById(string id) => mods.FirstOrDefault(m => m.Meta != null
            && (string.Equals(m.Meta.ModId, id, StringComparison.OrdinalIgnoreCase) || m.Meta.Provides.Contains(id, StringComparer.OrdinalIgnoreCase)));
        ContentEntry? ByConfig(string config) => mods.FirstOrDefault(m => m.Meta?.Mixins.Contains(config, StringComparer.OrdinalIgnoreCase) == true);
        ContentEntry? ByClass(string cls) => mods.FirstOrDefault(m => m.Meta?.Packages.Any(p => cls.StartsWith(p + ".", StringComparison.Ordinal)) == true);

        foreach (var id in primary)
            Add(ById(id));
        // Gescheiterte Konfiguration ("Mixin [sodium.mixins.json:…] … FAILED")
        foreach (var line in errorLines)
            foreach (Match m in Regex.Matches(line, @"Mixin \[([A-Za-z0-9_.\-]+\.mixins?\.json)"))
                Add(ByConfig(m.Groups[1].Value));
        // Konfliktpartner
        foreach (var line in errorLines)
        {
            foreach (Match m in Regex.Matches(line, @"(?:merged by|already redirected by|conflicts? with) ([a-zA-Z_][\w$]*(?:\.[\w$]+){2,})"))
                Add(ByClass(m.Groups[1].Value) ?? ByConfig(m.Groups[1].Value));
            foreach (Match m in Regex.Matches(line, @"([A-Za-z0-9_.\-]+\.mixins?\.json)"))
                Add(ByConfig(m.Groups[1].Value));
        }
        foreach (var id in others)
            Add(ById(id));
        return hits.Take(3).ToList();
    }

    [GeneratedRegex(@"FATAL|/ERROR|Mixin apply for mod|MixinApplyError|InvalidInjectionException|InvalidMixinException|@Redirect conflict|Caused by|cannot inject|merged by")]
    private static partial Regex MixinErrorLine();

    /// <summary>Fabric: "Mod 'X' (x) 1.0 requires …" bzw. "- Mod 'X' (x) … is incompatible".</summary>
    internal static List<CrashSuspect> FindByResolution(string text, List<ContentEntry> mods)
    {
        var ids = Regex.Matches(text, @"Mod '[^']+' \(([a-z0-9_\-]+)\)").Select(m => m.Groups[1].Value).ToHashSet();
        return mods.Where(m => m.Meta?.ModId != null && ids.Contains(m.Meta.ModId)).Select(m => new CrashSuspect(m.File, m.Name)).ToList();
    }

    /// <summary>Forge/NeoForge: "Suspected Mods: X (x), Y (y)".</summary>
    internal static List<CrashSuspect> FindForgeSuspects(string crash, List<ContentEntry> mods)
    {
        var line = Regex.Match(crash, @"Suspected Mods?:\s*(.+)");
        if (!line.Success || line.Groups[1].Value.Contains("NONE"))
            return [];
        var ids = Regex.Matches(line.Groups[1].Value, @"\(([a-z0-9_\-]+)\)").Select(m => m.Groups[1].Value).ToHashSet();
        return mods.Where(m => m.Meta?.ModId != null && ids.Contains(m.Meta.ModId)).Select(m => new CrashSuspect(m.File, m.Name)).ToList();
    }

    private static string Trim(string? text) =>
        string.IsNullOrEmpty(text) ? "" : text.Length > MaxText ? text[^MaxText..] : text;

    /// <summary>Liest neue Crash-Reports und hs_err-Dateien seit dem Start.</summary>
    public static (string? CrashText, string? CrashFile, string? HsErr) CollectFiles(string gameDir, DateTimeOffset since)
    {
        string? crashText = null, crashFile = null, hsErr = null;
        var reports = Path.Combine(gameDir, "crash-reports");
        if (Directory.Exists(reports))
        {
            var newest = new DirectoryInfo(reports).EnumerateFiles("crash-*.txt")
                .Where(f => f.LastWriteTimeUtc >= since.UtcDateTime.AddSeconds(-5))
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (newest != null)
            {
                crashFile = newest.Name;
                crashText = ReadLimited(newest.FullName);
            }
        }
        var hs = Directory.Exists(gameDir)
            ? new DirectoryInfo(gameDir).EnumerateFiles("hs_err_pid*.log")
                .Where(f => f.LastWriteTimeUtc >= since.UtcDateTime.AddSeconds(-5))
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (hs != null)
            hsErr = ReadLimited(hs.FullName);
        return (crashText, crashFile, hsErr);
    }

    private static string ReadLimited(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > MaxText)
            stream.Seek(-MaxText, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
