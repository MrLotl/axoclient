using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace McLauncher;

/// <summary>Eine erkannte Ursache mit optionaler Maßnahme, die der Launcher selbst ausführen kann.</summary>
public sealed class CrashFinding
{
    public required string Title { get; init; }
    public required string Description { get; init; }

    /// <summary>Was die Maßnahme tut (null = nur Hinweis).</summary>
    public string? FixText { get; init; }

    /// <summary>Führt die Maßnahme aus und liefert eine Ergebniszeile.</summary>
    public Func<IProgress<string>, Task<string>>? Fix { get; init; }
}

public sealed class CrashReport
{
    /// <summary>Datei, aus der gelesen wurde (Crash-Bericht oder Log), oder null.</summary>
    public string? SourceFile { get; init; }
    public DateTime? When { get; init; }
    public List<CrashFinding> Findings { get; } = [];
}

/// <summary>
/// Liest Crash-Bericht und Log einer Instanz, erkennt bekannte Ursachen (zu wenig Speicher, unpassende Mods,
/// falsche JVM-Argumente, Grafikfehler) und bietet Maßnahmen an, die direkt im Launcher ausgeführt werden.
/// </summary>
public static class CrashAnalyzer
{
    private const int MaxRead = 600_000;
    private static readonly HashSet<string> NeverDisable = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "java", "fabricloader", "fabric-loader", "forge", "neoforge", "fabric-api", "fabric",
        "mixinextras", "axoclient"
    };

    public static CrashReport? Analyze(AppState app, Installation inst)
    {
        var (file, when, text) = ReadLatest(inst);
        if (file == null)
            return null;

        var report = new CrashReport { SourceFile = file, When = when };
        var settings = app.Settings;

        // 1. Zu wenig (oder zu viel) Arbeitsspeicher
        if (Regex.IsMatch(text, @"OutOfMemoryError|Java heap space|GC overhead limit|Could not reserve enough space|Invalid maximum heap size"))
        {
            var current = RamAdvisor.CurrentMb(inst, settings);
            var advice = RamAdvisor.Recommend(inst);
            var tooMuch = Regex.IsMatch(text, @"Could not reserve enough space|Invalid maximum heap size");
            var target = tooMuch
                ? Math.Min(advice.Mb, Math.Max(2048, RamAdvisor.TotalMb() / 2 / 512 * 512))
                : Math.Max(advice.Mb, Math.Min(current + 1024, Math.Max(2048, RamAdvisor.TotalMb() * 7 / 10 / 512 * 512)));
            if (target != current)
                report.Findings.Add(new CrashFinding
                {
                    Title = tooMuch ? "Zu viel Arbeitsspeicher eingestellt" : "Minecraft ist der Speicher ausgegangen",
                    Description = $"Diese Instanz hat {RamAdvisor.Format(current)} zugewiesen; empfohlen sind " +
                                  $"{RamAdvisor.Format(target)} ({advice.Reason}).",
                    FixText = $"Arbeitsspeicher der Instanz auf {RamAdvisor.Format(target)} setzen",
                    Fix = _ =>
                    {
                        inst.MaxRamMb = target;
                        app.NotifyInstallationsChanged();
                        return Task.FromResult($"Arbeitsspeicher: {RamAdvisor.Format(target)}.");
                    }
                });
            else
                report.Findings.Add(new CrashFinding
                {
                    Title = "Minecraft ist der Speicher ausgegangen",
                    Description = "Der Arbeitsspeicher ist schon auf dem empfohlenen Wert. Entferne Mods, Shader oder " +
                                  "hochauflösende Ressourcenpakete oder verringere die Sichtweite im Spiel."
                });
        }

        // 2. Ungültige JVM-Argumente
        if (Regex.IsMatch(text, @"Unrecognized (VM )?option|Could not create the Java Virtual Machine")
            && !string.IsNullOrWhiteSpace(settings.JvmArguments))
            report.Findings.Add(new CrashFinding
            {
                Title = "Ungültige Java-Argumente",
                Description = $"Die Java-Virtual-Machine hat eine Option nicht akzeptiert. Eingestellt: {settings.JvmArguments}",
                FixText = "Zusätzliche Java-Argumente leeren",
                Fix = _ =>
                {
                    settings.JvmArguments = "";
                    app.NotifyInstallationsChanged();
                    return Task.FromResult("Java-Argumente geleert.");
                }
            });

        // 3. Falsche Java-Version
        if (text.Contains("UnsupportedClassVersionError") || text.Contains("compiled by a more recent version of the Java"))
            report.Findings.Add(new CrashFinding
            {
                Title = "Mod für eine neuere Java-Version",
                Description = "Eine Mod braucht eine neuere Java-Version als die, mit der Minecraft läuft. Meist ist die Mod " +
                              "für eine andere Minecraft-Version gebaut."
            });

        // 4. Verdächtige Mods aus Bericht/Log
        var suspects = SuspectIds(text);
        if (suspects.Count > 0 && inst.Loader != LoaderType.Vanilla)
        {
            var store = new ContentStore(inst, app.Http);
            var byId = ModsById(store);
            var found = suspects.Where(byId.ContainsKey).Select(id => byId[id]).Distinct().ToList();
            if (found.Count > 0)
            {
                var names = string.Join(", ", found.Select(f => f.DisplayName));
                report.Findings.Add(new CrashFinding
                {
                    Title = found.Count == 1 ? $"Verdächtige Mod: {found[0].DisplayName}" : "Verdächtige Mods",
                    Description = "Laut Crash-Bericht ist beteiligt: " + names + ". Deaktivieren ist umkehrbar " +
                                  "(unter Mods wieder einschalten).",
                    FixText = $"Deaktivieren: {names}",
                    Fix = _ =>
                    {
                        foreach (var item in found)
                            store.SetEnabled(item, false);
                        return Task.FromResult($"Deaktiviert: {names}.");
                    }
                });
            }
        }

        // 5. Mods passen nicht zusammen: der Mod-Check des Launchers repariert das
        if (inst.Loader != LoaderType.Vanilla && Regex.IsMatch(text,
                @"Incompatible mod set|Mod resolution failed|which is missing|Could not find required mod|" +
                @"[Dd]uplicate mod|Found duplicate|Missing or unsupported mandatory dependencies|" +
                @"requires (any )?version|is incompatible with|MissingModsException|ModResolutionException"))
            report.Findings.Add(new CrashFinding
            {
                Title = "Mods passen nicht zusammen",
                Description = "Fehlende Abhängigkeiten, doppelte oder zur Minecraft-Version unpassende Mods.",
                FixText = "Mods prüfen und automatisch reparieren (fehlende ergänzen, unpassende ersetzen)",
                Fix = progress => RepairModsAsync(app, inst, progress)
            });

        // 6. Grafik
        if (Regex.IsMatch(text, @"GLFW error|Pixel format not accepted|Failed to create window|GL_OUT_OF_MEMORY"))
            report.Findings.Add(new CrashFinding
            {
                Title = "Grafikproblem",
                Description = "Minecraft konnte das Fenster oder OpenGL nicht starten. Aktualisiere den Grafiktreiber " +
                              "und starte nicht über Remote-Desktop. Bei Shadern: probiere es ohne Shader."
            });

        // Nichts erkannt: allgemeiner Mod-Check als Ausweg
        if (report.Findings.Count == 0)
        {
            var hasMods = inst.Loader != LoaderType.Vanilla;
            report.Findings.Add(new CrashFinding
            {
                Title = "Ursache nicht eindeutig",
                Description = hasMods
                    ? "Aus dem Bericht lässt sich keine bekannte Ursache ablesen. Ein Mod-Check findet trotzdem " +
                      "häufige Fehler (falsche Version, fehlende Abhängigkeiten)."
                    : "Aus dem Bericht lässt sich keine bekannte Ursache ablesen.",
                FixText = hasMods ? "Mods prüfen und automatisch reparieren" : null,
                Fix = hasMods ? progress => RepairModsAsync(app, inst, progress) : null
            });
        }
        return report;
    }

    /// <summary>Wendet alle automatischen Lösungen des Mod-Checks an.</summary>
    private static async Task<string> RepairModsAsync(AppState app, Installation inst, IProgress<string> progress)
    {
        var repair = new ModRepair(new ContentStore(inst, app.Http), new ModrinthProvider(app.Http), null);
        var issues = await repair.AnalyzeAsync(progress);
        var fixable = issues.Where(i => i.CanFix).ToList();
        var done = 0;
        var failed = new List<string>();
        foreach (var issue in fixable)
        {
            try
            {
                progress.Report(issue.FixText ?? issue.Title);
                await issue.Fix!(progress);
                done++;
            }
            catch (Exception ex)
            {
                failed.Add($"{issue.Title}: {ex.Message}");
            }
        }
        if (fixable.Count == 0)
            return "Mod-Check: keine automatisch behebbaren Probleme gefunden.";
        return $"Mod-Check: {done} von {fixable.Count} Problem(en) behoben." +
               (failed.Count > 0 ? " Fehler: " + string.Join("; ", failed) : "");
    }

    // ---------- Lesen ----------

    private static (string? file, DateTime? when, string text) ReadLatest(Installation inst)
    {
        var candidates = new List<string>();
        var crashes = Path.Combine(inst.GameDir, "crash-reports");
        if (Directory.Exists(crashes))
            candidates.AddRange(Directory.GetFiles(crashes, "crash-*.txt"));
        if (Directory.Exists(inst.GameDir))
            candidates.AddRange(Directory.GetFiles(inst.GameDir, "hs_err_pid*.log"));
        var newestCrash = candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        var log = Path.Combine(inst.GameDir, "logs", "latest.log");

        // Der Bericht gilt nur, wenn er nicht uralt ist
        string? main = null;
        if (newestCrash != null)
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(newestCrash);
            if (age < TimeSpan.FromDays(14) || !File.Exists(log))
                main = newestCrash;
        }
        main ??= File.Exists(log) ? log : null;
        if (main == null)
            return (null, null, "");

        var text = Tail(main);
        if (main != log && File.Exists(log))
            text += "\n" + Tail(log);
        return (main, File.GetLastWriteTime(main), text);
    }

    private static string Tail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > MaxRead)
                stream.Seek(-MaxRead, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }

    // ---------- Mods ----------

    private static HashSet<string> SuspectIds(string text)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Crashberichte: "Suspected Mods:" gefolgt von "\tName (modid)"
        var block = Regex.Match(text, @"Suspected Mods?:\s*\r?\n((?:[ \t]+.*\r?\n?)+)");
        if (block.Success)
            foreach (Match m in Regex.Matches(block.Groups[1].Value, @"\(([A-Za-z0-9_\-]+)\)"))
                ids.Add(m.Groups[1].Value);

        // Mixin-Fehler nennen die Konfiguration der Mod: "mixins.<modid>.json" bzw. "<modid>.mixins.json"
        if (Regex.IsMatch(text, @"Mixin apply failed|InvalidMixinException|MixinTransformerError|Critical injection failure"))
            foreach (Match m in Regex.Matches(text, @"(?:mixins?\.([a-z0-9_\-]+)\.json|([a-z0-9_\-]+)\.mixins?\.json)"))
                ids.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

        // Fabric-Meldungen: Mod 'Name' (id) ...
        foreach (Match m in Regex.Matches(text, @"Mod '[^']+' \(([a-z0-9_\-]+)\)"))
            ids.Add(m.Groups[1].Value);

        ids.ExceptWith(NeverDisable);
        return ids;
    }

    private static Dictionary<string, InstalledItem> ModsById(ContentStore store)
    {
        var map = new Dictionary<string, InstalledItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in store.GetInstalled(ContentType.Mod).Where(i => i.Enabled))
        {
            try
            {
                using var zip = ZipFile.OpenRead(item.FullPath);
                foreach (var id in ModIds(zip))
                    map.TryAdd(id, item);
            }
            catch
            {
                // keine lesbare JAR-Datei
            }
        }
        return map;
    }

    private static List<string> ModIds(ZipArchive zip)
    {
        var ids = new List<string>();
        if (zip.GetEntry("fabric.mod.json") is { } fabric)
        {
            using var reader = new StreamReader(fabric.Open());
            var m = Regex.Match(reader.ReadToEnd(), "\"id\"\\s*:\\s*\"([a-z0-9_\\-]+)\"");
            if (m.Success)
                ids.Add(m.Groups[1].Value);
        }
        foreach (var name in new[] { "META-INF/mods.toml", "META-INF/neoforge.mods.toml" })
            if (zip.GetEntry(name) is { } toml)
            {
                using var reader = new StreamReader(toml.Open());
                foreach (Match m in Regex.Matches(reader.ReadToEnd(), "modId\\s*=\\s*\"([A-Za-z0-9_\\-]+)\""))
                    ids.Add(m.Groups[1].Value);
            }
        return ids;
    }
}
