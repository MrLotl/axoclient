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
            _rules = rules.Rules.Select(rule => (rule, rule.Match.Select(patternText =>
                new Regex(patternText, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1))).ToArray())).ToList();
        }

        public CrashRuleSet LoadEmbeddedRules()
        {
            using var stream = typeof(CrashAnalyzer).Assembly.GetManifestResourceStream("crash-rules.json")
                               ?? throw new InvalidOperationException("crash-rules.json fehlt");
            return JsonSerializer.Deserialize<CrashRuleSet>(stream, _jsonHelper.Options)!;
        }

        public IReadOnlyList<CrashRule> Rules => _rules.Select(rule => rule.Rule).ToList();

        public CrashAnalysis Analyze(CrashInput input, ContentDocument? content, bool server = false)
        {
            var sources = new Dictionary<string, string>
            {
                ["crash"] = trim(input.CrashReport),
                ["hserr"] = trim(input.HsErr),
                ["log"] = trim(input.LogTail)
            };
            // Nur aktive Mods: deaktivierte können den Absturz nicht ausgelöst haben
            var mods = content?.Mods.Where(entry => entry.Enabled && entry.Source != ContentSource.AxoClient).ToList() ?? [];

            foreach (var (rule, patterns) in _rules.Where(rule => rule.Rule.Server == server))
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
                        match = patterns.Select(pattern => pattern.Match(text)).FirstOrDefault(candidate => candidate.Success);
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
                    var file = match.Groups["file"] is { Success: true } fileGroup ? fileGroup.Value : null;
                    // Nennt die Regel eine Mod-Datei, ist genau diese Mod gemeint
                    if (suspects.Count == 0 && file != null && mods.FirstOrDefault(mod => mod.File.Equals(file, StringComparison.OrdinalIgnoreCase)) is { } byFile)
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

            var recent = markRollback(mods.OrderByDescending(mod => mod.AddedAt).Take(3).Select(mod => new CrashSuspect(mod.File, mod.Name)).ToList(), mods);
            var stack = markRollback(FindByStacktrace(string.Join('\n', sources.Values), mods), mods);
            return new CrashAnalysis(false, new TextRef("crash.cause.unknown"), null,
                withRollback(mods.Count > 0 ? ["checkMods"] : [], stack.Count > 0 ? stack : recent), stack, recent,
                null, input.CrashReportFile, input.ExitCode);
        }

        private List<CrashSuspect> markRollback(List<CrashSuspect> suspects, List<ContentEntry> mods) =>
            suspects.Select(suspect => suspect with { CanRollback = mods.Any(mod => mod.File == suspect.File && mod.Previous != null) }).ToList();

        /// <summary>Wurde eine beteiligte Mod gerade aktualisiert, ist das Zurücknehmen die naheliegendste Maßnahme.</summary>
        private IReadOnlyList<string> withRollback(IReadOnlyList<string> actions, List<CrashSuspect> suspects) =>
            suspects.Any(suspect => suspect.CanRollback) && !actions.Contains("rollbackMod") ? ["rollbackMod", .. actions] : actions;

        /// <summary>Paketnamen im Stacktrace den Mods zuordnen (Pakete merkt sich der Launcher beim Installieren).</summary>
        internal List<CrashSuspect> FindByStacktrace(string text, List<ContentEntry> mods)
        {
            var frames = Regex.Matches(text, @"at ([a-zA-Z_][\w$]*(?:\.[a-zA-Z_][\w$]*){2,})\.").Select(match => match.Groups[1].Value)
                .Where(frame => !frame.StartsWith("java.") && !frame.StartsWith("jdk.") && !frame.StartsWith("sun.") && !frame.StartsWith("net.minecraft.")
                            && !frame.StartsWith("com.mojang.") && !frame.StartsWith("net.fabricmc.loader") && !frame.StartsWith("org.spongepowered")
                            && !frame.StartsWith("cpw.mods") && !frame.StartsWith("net.neoforged.fml") && !frame.StartsWith("net.minecraftforge.fml"))
                .Take(60)
                .ToList();
            var hits = new List<CrashSuspect>();
            foreach (var frame in frames)
            {
                var mod = mods.FirstOrDefault(candidateMod => candidateMod.Meta?.Packages.Any(package => frame.StartsWith(package + ".", StringComparison.Ordinal)) == true);
                if (mod != null && hits.All(hit => hit.File != mod.File))
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
                .Where(part => !part.TrimStart().StartsWith("at ", StringComparison.Ordinal))
                .Where(part => mixinErrorLine().IsMatch(part))
                .ToList();
            var primary = new List<string>();
            var others = new List<string>();
            foreach (var line in errorLines)
            {
                foreach (Match match in Regex.Matches(line, @"Mixin apply for mod ([a-z0-9_]+(?:-[a-z0-9_]+)*) failed"))
                {
                    primary.Add(match.Groups[1].Value);
                }

                foreach (Match match in Regex.Matches(line, @"from mod ([a-z0-9_]+(?:-[a-z0-9_]+)*)"))
                {
                    others.Add(match.Groups[1].Value);
                }
            }

            var hits = new List<CrashSuspect>();
            void Add(ContentEntry? mod)
            {
                if (mod != null && hits.All(hit => hit.File != mod.File))
                {
                    hits.Add(new CrashSuspect(mod.File, mod.Name));
                }
            }
            ContentEntry? ById(string id) => mods.FirstOrDefault(mod => mod.Meta != null
                && (string.Equals(mod.Meta.ModId, id, StringComparison.OrdinalIgnoreCase) || mod.Meta.Provides.Contains(id, StringComparer.OrdinalIgnoreCase)));
            ContentEntry? ByConfig(string config) => mods.FirstOrDefault(mod => mod.Meta?.Mixins.Contains(config, StringComparer.OrdinalIgnoreCase) == true);
            ContentEntry? ByClass(string cls) => mods.FirstOrDefault(mod => mod.Meta?.Packages.Any(package => cls.StartsWith(package + ".", StringComparison.Ordinal)) == true);

            foreach (var id in primary)
            {
                Add(ById(id));
            }
            // Gescheiterte Konfiguration ("Mixin [sodium.mixins.json:…] … FAILED")
            foreach (var line in errorLines)
            {
                foreach (Match match in Regex.Matches(line, @"Mixin \[([A-Za-z0-9_.\-]+\.mixins?\.json)"))
                {
                    Add(ByConfig(match.Groups[1].Value));
                }
            }
            // Konfliktpartner
            foreach (var line in errorLines)
            {
                foreach (Match match in Regex.Matches(line, @"(?:merged by|already redirected by|conflicts? with) ([a-zA-Z_][\w$]*(?:\.[\w$]+){2,})"))
                {
                    Add(ByClass(match.Groups[1].Value) ?? ByConfig(match.Groups[1].Value));
                }

                foreach (Match match in Regex.Matches(line, @"([A-Za-z0-9_.\-]+\.mixins?\.json)"))
                {
                    Add(ByConfig(match.Groups[1].Value));
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
            var ids = Regex.Matches(text, @"Mod '[^']+' \(([a-z0-9_\-]+)\)").Select(match => match.Groups[1].Value).ToHashSet();
            return mods.Where(mod => mod.Meta?.ModId != null && ids.Contains(mod.Meta.ModId)).Select(mod => new CrashSuspect(mod.File, mod.Name)).ToList();
        }

        /// <summary>
        /// Forge/NeoForge, fehlende Abhängigkeit: die Mod, die etwas verlangt ("Requested by: 'x'" bzw. "Mod X requires …"),
        /// nicht die fehlende – die ist ja gerade nicht installiert.
        /// </summary>
        internal List<CrashSuspect> FindByForgeDependency(string text, List<ContentEntry> mods)
        {
            var ids = Regex.Matches(text, @"Requested by: '([a-z0-9_\-]+)'").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var names = Regex.Matches(text, @"Mod (.+?) requires ").Select(match => match.Groups[1].Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return mods.Where(mod => (mod.Meta?.ModId != null && ids.Contains(mod.Meta.ModId)) || names.Contains(mod.Name))
                .Select(mod => new CrashSuspect(mod.File, mod.Name)).Take(3).ToList();
        }

        /// <summary>Forge/NeoForge: "Suspected Mods: X (x), Y (y)".</summary>
        internal List<CrashSuspect> FindForgeSuspects(string crash, List<ContentEntry> mods)
        {
            var line = Regex.Match(crash, @"Suspected Mods?:\s*(.+)");
            if (!line.Success || line.Groups[1].Value.Contains("NONE"))
            {
                return [];
            }

            var ids = Regex.Matches(line.Groups[1].Value, @"\(([a-z0-9_\-]+)\)").Select(match => match.Groups[1].Value).ToHashSet();
            return mods.Where(mod => mod.Meta?.ModId != null && ids.Contains(mod.Meta.ModId)).Select(mod => new CrashSuspect(mod.File, mod.Name)).ToList();
        }

        private string trim(string? text) =>
            string.IsNullOrEmpty(text) ? "" : text.Length > MaxText ? text[^MaxText..] : text;

        /// <summary>Liest neue Crash-Reports und hs_err-Dateien seit dem Start.</summary>
        public (string? CrashText, string? CrashFile, string? HsErr) CollectFiles(string gameDirectory, DateTimeOffset since)
        {
            string? crashText = null, crashFile = null, hsErr = null;
            var reports = Path.Combine(gameDirectory, "crash-reports");
            if (Directory.Exists(reports))
            {
                var newest = new DirectoryInfo(reports).EnumerateFiles("crash-*.txt")
                    .Where(file => file.LastWriteTimeUtc >= since.UtcDateTime.AddSeconds(-5))
                    .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
                if (newest != null)
                {
                    crashFile = newest.Name;
                    crashText = readLimited(newest.FullName);
                }
            }
            var crashLogFile = Directory.Exists(gameDirectory)
                ? new DirectoryInfo(gameDirectory).EnumerateFiles("hs_err_pid*.log")
                    .Where(file => file.LastWriteTimeUtc >= since.UtcDateTime.AddSeconds(-5))
                    .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (crashLogFile != null)
            {
                hsErr = readLimited(crashLogFile.FullName);
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
