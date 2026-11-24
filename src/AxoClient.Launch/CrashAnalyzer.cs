using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Common;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Launch;
using AxoClient.Launch.Contract;

namespace AxoClient.Launch
{
    /// <summary>
    /// Sucht per Regeln aus <c>crash-rules.json</c> die wahrscheinliche Ursache. Eine Ursache wird nur genannt, wenn eine
    /// Regel sicher passt; sonst "Ursache unbekannt" mit den zuletzt hinzugefügten Mods als mögliche Auslöser.
    /// </summary>
    public sealed partial class CrashAnalyzer : ICrashAnalyzer
    {
        private readonly IJsonHelper _jsonHelper;
        private const int MaxText = 600_000;
        private readonly IReadOnlyList<(CrashRule Rule, Regex[] Patterns)> _rules;

        public CrashAnalyzer(IJsonHelper jsonHelper)
            : this(jsonHelper, null)
        {
        }

        public CrashAnalyzer(IJsonHelper jsonHelper, CrashRuleSet? rules)
        {
            _jsonHelper = jsonHelper;
            rules ??= LoadEmbeddedRules();
            _rules = rules.Rules.Select(r => (r, r.Match.Select(m =>
                new Regex(m, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1))).ToArray())).ToList();
        }

        public CrashRuleSet LoadEmbeddedRules()
        {
            using var stream = typeof(CrashAnalyzer).Assembly.GetManifestResourceStream("crash-rules.json")
                               ?? throw new InvalidOperationException("crash-rules.json fehlt");
            return JsonSerializer.Deserialize<CrashRuleSet>(stream, _jsonHelper.Options)!;
        }

        public IReadOnlyList<CrashRule> Rules => _rules.Select(r => r.Rule).ToList();

        public CrashAnalysis Analyze(CrashInput input, ContentDocument? content, bool server = false)
        {
            var sources = new Dictionary<string, string>
            {
                ["crash"] = trim(input.CrashReport),
                ["hserr"] = trim(input.HsErr),
                ["log"] = trim(input.LogTail)
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
                    {
                        continue;
                    }

                    Match? match = null;
                    try
                    {
                        match = patterns.Select(p => p.Match(text)).FirstOrDefault(m => m.Success);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                    }
                    if (match == null)
                    {
                        continue;
                    }

                    var allText = string.Join('\n', sources.Values);
                    var suspects = rule.FindMod switch
                    {
                        "mixin" => FindByMixin(allText, mods),
                        "fabricResolution" => FindByResolution(allText, mods),
                        "stacktrace" => FindByStacktrace(allText, mods),
                        "forgeDependency" => FindByForgeDependency(allText, mods),
                        _ => []
                    };
                    var file = match.Groups["file"] is { Success: true } g ? g.Value : null;
                    // Nennt die Regel eine Mod-Datei, ist genau diese Mod gemeint
                    if (suspects.Count == 0 && file != null && mods.FirstOrDefault(m => m.File.Equals(file, StringComparison.OrdinalIgnoreCase)) is { } byFile)
                    {
                        suspects = [new CrashSuspect(byFile.File, byFile.Name)];
                    }
                    // Forge/NeoForge nennen verdächtige Mods selbst
                    if (suspects.Count == 0)
                    {
                        suspects = FindForgeSuspects(sources["crash"], mods);
                    }
                    // Zwei Mods beißen sich: beide nennen, statt nur "verträgt sich nicht"
                    var cause = rule.FindMod == "mixin" && suspects.Count >= 2
                        ? new TextRef("crash.cause.mixinConflict", ("mod", suspects[0].Name), ("other", suspects[1].Name))
                        : new TextRef(rule.Cause, ("file", file), ("mod", suspects.FirstOrDefault()?.Name));
                    suspects = markRollback(suspects, mods);
                    return new CrashAnalysis(true, cause,
                        rule.Id, withRollback(rule.Actions, suspects), suspects, [], file, input.CrashReportFile, input.ExitCode);
                }
            }

            var recent = markRollback(mods.OrderByDescending(m => m.AddedAt).Take(3).Select(m => new CrashSuspect(m.File, m.Name)).ToList(), mods);
            var stack = markRollback(FindByStacktrace(string.Join('\n', sources.Values), mods), mods);
            return new CrashAnalysis(false, new TextRef("crash.cause.unknown"), null,
                withRollback(mods.Count > 0 ? ["checkMods"] : [], stack.Count > 0 ? stack : recent), stack, recent,
                null, input.CrashReportFile, input.ExitCode);
        }

        private List<CrashSuspect> markRollback(List<CrashSuspect> suspects, List<ContentEntry> mods) =>
            suspects.Select(s => s with { CanRollback = mods.Any(m => m.File == s.File && m.Previous != null) }).ToList();

        /// <summary>Wurde eine beteiligte Mod gerade aktualisiert, ist das Zurücknehmen die naheliegendste Maßnahme.</summary>
        private IReadOnlyList<string> withRollback(IReadOnlyList<string> actions, List<CrashSuspect> suspects) =>
            suspects.Any(s => s.CanRollback) && !actions.Contains("rollbackMod") ? ["rollbackMod", .. actions] : actions;

        /// <summary>Paketnamen im Stacktrace den Mods zuordnen (Pakete merkt sich der Launcher beim Installieren).</summary>
        internal List<CrashSuspect> FindByStacktrace(string text, List<ContentEntry> mods)
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
                {
                    hits.Add(new CrashSuspect(mod.File, mod.Name));
                }

                if (hits.Count >= 3)
                {
                    break;
                }
            }
            return hits;
        }

        /// <summary>
        /// Mixin-Fehler: nur die eigentlichen Fehlerzeilen auswerten. Beim normalen Laden nennt das Log die Mixin-Konfiguration
        /// jeder Mod; wer das ganze Log durchsucht, verdächtigt am Ende alle. Reihenfolge: die Mod, deren Mixin scheitert,
        /// danach die Mod(s), mit denen sie sich beißt ("merged by", "already redirected by", "@Redirect conflict").
        /// </summary>
        internal List<CrashSuspect> FindByMixin(string text, List<ContentEntry> mods)
        {
            var errorLines = text.Split('\n')
                .Where(l => !l.TrimStart().StartsWith("at ", StringComparison.Ordinal))
                .Where(l => mixinErrorLine().IsMatch(l))
                .ToList();
            var primary = new List<string>();
            var others = new List<string>();
            foreach (var line in errorLines)
            {
                foreach (Match m in Regex.Matches(line, @"Mixin apply for mod ([a-z0-9_]+(?:-[a-z0-9_]+)*) failed"))
                {
                    primary.Add(m.Groups[1].Value);
                }

                foreach (Match m in Regex.Matches(line, @"from mod ([a-z0-9_]+(?:-[a-z0-9_]+)*)"))
                {
                    others.Add(m.Groups[1].Value);
                }
            }

            var hits = new List<CrashSuspect>();
            void Add(ContentEntry? mod)
            {
                if (mod != null && hits.All(h => h.File != mod.File))
                {
                    hits.Add(new CrashSuspect(mod.File, mod.Name));
                }
            }
            ContentEntry? ById(string id) => mods.FirstOrDefault(m => m.Meta != null
                && (string.Equals(m.Meta.ModId, id, StringComparison.OrdinalIgnoreCase) || m.Meta.Provides.Contains(id, StringComparer.OrdinalIgnoreCase)));
            ContentEntry? ByConfig(string config) => mods.FirstOrDefault(m => m.Meta?.Mixins.Contains(config, StringComparer.OrdinalIgnoreCase) == true);
            ContentEntry? ByClass(string cls) => mods.FirstOrDefault(m => m.Meta?.Packages.Any(p => cls.StartsWith(p + ".", StringComparison.Ordinal)) == true);

            foreach (var id in primary)
            {
                Add(ById(id));
            }
            // Gescheiterte Konfiguration ("Mixin [sodium.mixins.json:…] … FAILED")
            foreach (var line in errorLines)
            {
                foreach (Match m in Regex.Matches(line, @"Mixin \[([A-Za-z0-9_.\-]+\.mixins?\.json)"))
                {
                    Add(ByConfig(m.Groups[1].Value));
                }
            }
            // Konfliktpartner
            foreach (var line in errorLines)
            {
                foreach (Match m in Regex.Matches(line, @"(?:merged by|already redirected by|conflicts? with) ([a-zA-Z_][\w$]*(?:\.[\w$]+){2,})"))
                {
                    Add(ByClass(m.Groups[1].Value) ?? ByConfig(m.Groups[1].Value));
                }

                foreach (Match m in Regex.Matches(line, @"([A-Za-z0-9_.\-]+\.mixins?\.json)"))
                {
                    Add(ByConfig(m.Groups[1].Value));
                }
            }
            foreach (var id in others)
            {
                Add(ById(id));
            }

            return hits.Take(3).ToList();
        }

        [GeneratedRegex(@"FATAL|/ERROR|Mixin apply for mod|MixinApplyError|InvalidInjectionException|InvalidMixinException|@Redirect conflict|Caused by|cannot inject|merged by")]
        private partial Regex mixinErrorLine();

        /// <summary>Fabric: "Mod 'X' (x) 1.0 requires …" bzw. "- Mod 'X' (x) … is incompatible".</summary>
        internal List<CrashSuspect> FindByResolution(string text, List<ContentEntry> mods)
        {
            var ids = Regex.Matches(text, @"Mod '[^']+' \(([a-z0-9_\-]+)\)").Select(m => m.Groups[1].Value).ToHashSet();
            return mods.Where(m => m.Meta?.ModId != null && ids.Contains(m.Meta.ModId)).Select(m => new CrashSuspect(m.File, m.Name)).ToList();
        }

        /// <summary>
        /// Forge/NeoForge, fehlende Abhängigkeit: die Mod, die etwas verlangt ("Requested by: 'x'" bzw. "Mod X requires …"),
        /// nicht die fehlende – die ist ja gerade nicht installiert.
        /// </summary>
        internal List<CrashSuspect> FindByForgeDependency(string text, List<ContentEntry> mods)
        {
            var ids = Regex.Matches(text, @"Requested by: '([a-z0-9_\-]+)'").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var names = Regex.Matches(text, @"Mod (.+?) requires ").Select(m => m.Groups[1].Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return mods.Where(m => (m.Meta?.ModId != null && ids.Contains(m.Meta.ModId)) || names.Contains(m.Name))
                .Select(m => new CrashSuspect(m.File, m.Name)).Take(3).ToList();
        }

        /// <summary>Forge/NeoForge: "Suspected Mods: X (x), Y (y)".</summary>
        internal List<CrashSuspect> FindForgeSuspects(string crash, List<ContentEntry> mods)
        {
            var line = Regex.Match(crash, @"Suspected Mods?:\s*(.+)");
            if (!line.Success || line.Groups[1].Value.Contains("NONE"))
            {
                return [];
            }

            var ids = Regex.Matches(line.Groups[1].Value, @"\(([a-z0-9_\-]+)\)").Select(m => m.Groups[1].Value).ToHashSet();
            return mods.Where(m => m.Meta?.ModId != null && ids.Contains(m.Meta.ModId)).Select(m => new CrashSuspect(m.File, m.Name)).ToList();
        }

        private string trim(string? text) =>
            string.IsNullOrEmpty(text) ? "" : text.Length > MaxText ? text[^MaxText..] : text;

        /// <summary>Liest neue Crash-Reports und hs_err-Dateien seit dem Start.</summary>
        public (string? CrashText, string? CrashFile, string? HsErr) CollectFiles(string gameDir, DateTimeOffset since)
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
                    crashText = readLimited(newest.FullName);
                }
            }
            var hs = Directory.Exists(gameDir)
                ? new DirectoryInfo(gameDir).EnumerateFiles("hs_err_pid*.log")
                    .Where(f => f.LastWriteTimeUtc >= since.UtcDateTime.AddSeconds(-5))
                    .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (hs != null)
            {
                hsErr = readLimited(hs.FullName);
            }

            return (crashText, crashFile, hsErr);
        }

        private string readLimited(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > MaxText)
            {
                stream.Seek(-MaxText, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
