using System.IO.Compression;
using System.Text.RegularExpressions;

namespace AxoClient.Game;

public sealed class CrashReport
{
    public string? SourceFile { get; init; }
    public DateTime? When { get; init; }
    public List<Issue> Findings { get; } = [];
}

public static class CrashAnalyzer
{
    private const int MaxRead = 600_000;

    private static readonly HashSet<string> NeverDisable = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "java", "fabricloader", "fabric-loader", "forge", "neoforge", "fabric-api", "fabric",
        "mixinextras", "axoclient"
    };

    public static CrashReport? Analyze(AppServices app, Installation inst)
    {
        var (file, when, text) = ReadLatest(inst);
        if (file == null)
            return null;

        var report = new CrashReport { SourceFile = file, When = when };
        CheckMemory(app, inst, text, report);
        CheckJvmArguments(app, text, report);
        CheckJavaVersion(text, report);
        CheckSuspectMods(app, inst, text, report);
        CheckModSet(app, inst, text, report);
        CheckGraphics(text, report);

        if (report.Findings.Count == 0)
            report.Findings.Add(new Issue
            {
                Severity = IssueSeverity.Info,
                Title = "Ursache nicht eindeutig",
                Description = inst.CanUseMods
                    ? "Aus dem Bericht lässt sich keine bekannte Ursache ablesen. Ein Mod-Check findet trotzdem " +
                      "häufige Fehler (falsche Version, fehlende Abhängigkeiten)."
                    : "Aus dem Bericht lässt sich keine bekannte Ursache ablesen.",
                FixText = inst.CanUseMods ? "Mods prüfen und automatisch reparieren" : null,
                Fix = inst.CanUseMods ? progress => RepairModsAsync(app, inst, progress) : null
            });
        return report;
    }

    private static void CheckMemory(AppServices app, Installation inst, string text, CrashReport report)
    {
        if (!Regex.IsMatch(text, @"OutOfMemoryError|Java heap space|GC overhead limit|Could not reserve enough space|Invalid maximum heap size"))
            return;

        var current = RamAdvisor.CurrentMb(inst, app.Settings);
        var advice = RamAdvisor.Recommend(inst);
        var total = RamAdvisor.TotalMb();
        var tooMuch = Regex.IsMatch(text, @"Could not reserve enough space|Invalid maximum heap size");
        var target = tooMuch
            ? Math.Min(advice.Mb, Math.Max(2048, total / 2 / 512 * 512))
            : Math.Max(advice.Mb, Math.Min(current + 1024, Math.Max(2048, total * 7 / 10 / 512 * 512)));

        report.Findings.Add(target != current
            ? new Issue
            {
                Severity = IssueSeverity.Error,
                Title = tooMuch ? "Zu viel Arbeitsspeicher eingestellt" : "Minecraft ist der Speicher ausgegangen",
                Description = $"Diese Instanz hat {Formats.Megabytes(current)} zugewiesen; empfohlen sind " +
                              $"{Formats.Megabytes(target)} ({advice.Reason}).",
                FixText = $"Arbeitsspeicher der Instanz auf {Formats.Megabytes(target)} setzen",
                Fix = Issue.Do(() =>
                {
                    inst.MaxRamMb = target;
                    app.Instances.NotifyChanged();
                }, $"Arbeitsspeicher: {Formats.Megabytes(target)}.")
            }
            : new Issue
            {
                Severity = IssueSeverity.Error,
                Title = "Minecraft ist der Speicher ausgegangen",
                Description = "Der Arbeitsspeicher ist schon auf dem empfohlenen Wert. Entferne Mods, Shader oder " +
                              "hochauflösende Ressourcenpakete oder verringere die Sichtweite im Spiel."
            });
    }

    private static void CheckJvmArguments(AppServices app, string text, CrashReport report)
    {
        if (!Regex.IsMatch(text, @"Unrecognized (VM )?option|Could not create the Java Virtual Machine")
            || string.IsNullOrWhiteSpace(app.Settings.JvmArguments))
            return;
        report.Findings.Add(new Issue
        {
            Severity = IssueSeverity.Error,
            Title = "Ungültige Java-Argumente",
            Description = $"Die Java-Virtual-Machine hat eine Option nicht akzeptiert. Eingestellt: {app.Settings.JvmArguments}",
            FixText = "Zusätzliche Java-Argumente leeren",
            Fix = Issue.Do(() =>
            {
                app.Settings.JvmArguments = "";
                app.Instances.NotifyChanged();
            }, "Java-Argumente geleert.")
        });
    }

    private static void CheckJavaVersion(string text, CrashReport report)
    {
        if (!text.Contains("UnsupportedClassVersionError") && !text.Contains("compiled by a more recent version of the Java"))
            return;
        report.Findings.Add(new Issue
        {
            Severity = IssueSeverity.Error,
            Title = "Mod für eine neuere Java-Version",
            Description = "Eine Mod braucht eine neuere Java-Version als die, mit der Minecraft läuft. Meist ist die Mod " +
                          "für eine andere Minecraft-Version gebaut."
        });
    }

    private static void CheckSuspectMods(AppServices app, Installation inst, string text, CrashReport report)
    {
        var suspects = SuspectIds(text);
        if (suspects.Count == 0 || !inst.CanUseMods)
            return;
        var store = app.ContentOf(inst);
        var byId = ModsById(store);
        var found = suspects.Where(byId.ContainsKey).Select(id => byId[id]).Distinct().ToList();
        if (found.Count == 0)
            return;

        var names = string.Join(", ", found.Select(f => f.DisplayName));
        report.Findings.Add(new Issue
        {
            Severity = IssueSeverity.Error,
            Title = found.Count == 1 ? $"Verdächtige Mod: {found[0].DisplayName}" : "Verdächtige Mods",
            Description = "Laut Crash-Bericht ist beteiligt: " + names + ". Deaktivieren ist umkehrbar " +
                          "(unter Mods wieder einschalten).",
            FixText = $"Deaktivieren: {names}",
            Fix = Issue.Do(() => found.ForEach(item => store.SetEnabled(item, false)), $"Deaktiviert: {names}.")
        });
    }

    private static void CheckModSet(AppServices app, Installation inst, string text, CrashReport report)
    {
        if (!inst.CanUseMods || !Regex.IsMatch(text,
                @"Incompatible mod set|Mod resolution failed|which is missing|Could not find required mod|" +
                @"[Dd]uplicate mod|Found duplicate|Missing or unsupported mandatory dependencies|" +
                @"requires (any )?version|is incompatible with|MissingModsException|ModResolutionException"))
            return;
        report.Findings.Add(new Issue
        {
            Severity = IssueSeverity.Error,
            Title = "Mods passen nicht zusammen",
            Description = "Fehlende Abhängigkeiten, doppelte oder zur Minecraft-Version unpassende Mods.",
            FixText = "Mods prüfen und automatisch reparieren (fehlende ergänzen, unpassende ersetzen)",
            Fix = progress => RepairModsAsync(app, inst, progress)
        });
    }

    private static void CheckGraphics(string text, CrashReport report)
    {
        if (!Regex.IsMatch(text, @"GLFW error|Pixel format not accepted|Failed to create window|GL_OUT_OF_MEMORY"))
            return;
        report.Findings.Add(new Issue
        {
            Severity = IssueSeverity.Error,
            Title = "Grafikproblem",
            Description = "Minecraft konnte das Fenster oder OpenGL nicht starten. Aktualisiere den Grafiktreiber " +
                          "und starte nicht über Remote-Desktop. Bei Shadern: probiere es ohne Shader."
        });
    }

    private static async Task<string> RepairModsAsync(AppServices app, Installation inst, WorkProgress progress)
    {
        var issues = await new ModRepair(app.ContentOf(inst), app.Modrinth).AnalyzeAsync(progress.Text);
        var fixable = issues.Where(i => i.CanFix).ToList();
        if (fixable.Count == 0)
            return "Mod-Check: keine automatisch behebbaren Probleme gefunden.";
        var (done, failed) = await Issue.FixAllAsync(fixable, progress);
        return $"Mod-Check: {done.Count} von {fixable.Count} Problem(en) behoben." +
               (failed.Count > 0 ? " Fehler: " + string.Join("; ", failed) : "");
    }

    private static (string? File, DateTime? When, string Text) ReadLatest(Installation inst)
    {
        var candidates = new List<string>();
        if (Directory.Exists(inst.CrashReportsDir))
            candidates.AddRange(Directory.GetFiles(inst.CrashReportsDir, "crash-*.txt"));
        if (Directory.Exists(inst.GameDir))
            candidates.AddRange(Directory.GetFiles(inst.GameDir, "hs_err_pid*.log"));
        var newestCrash = candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        var log = inst.LatestLog;

        string? main = null;
        if (newestCrash != null && (DateTime.UtcNow - File.GetLastWriteTimeUtc(newestCrash) < TimeSpan.FromDays(14)
                                    || !File.Exists(log)))
            main = newestCrash;
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
        catch (Exception ex)
        {
            ErrorReport.Log("Protokolldatei des Absturzes lesen", ex);
            return "";
        }
    }

    private static HashSet<string> SuspectIds(string text)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var block = Regex.Match(text, @"Suspected Mods?:\s*\r?\n((?:[ \t]+.*\r?\n?)+)");
        if (block.Success)
            foreach (Match m in Regex.Matches(block.Groups[1].Value, @"\(([A-Za-z0-9_\-]+)\)"))
                ids.Add(m.Groups[1].Value);

        if (Regex.IsMatch(text, @"Mixin apply failed|InvalidMixinException|MixinTransformerError|Critical injection failure"))
            foreach (Match m in Regex.Matches(text, @"(?:mixins?\.([a-z0-9_\-]+)\.json|([a-z0-9_\-]+)\.mixins?\.json)"))
                ids.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

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
            catch (Exception ex)
            {
                ErrorReport.Log("Mod-Datei untersuchen", ex);
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
