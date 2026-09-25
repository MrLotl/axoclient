using System.Text.RegularExpressions;

namespace AxoClient.Game;

public static class PreLaunchCheck
{
    private static readonly TimeSpan OnlineTimeout = TimeSpan.FromSeconds(8);
    private const int MaxHashedMods = 400;
    private const int MaxListed = 6;

    public static async Task<List<Issue>> RunAsync(AppServices app, Installation inst)
    {
        var results = new List<Issue>();
        results.AddRange(await Task.Run(() => CheckLocal(app, inst)));
        results.AddRange(await CheckModsOnlineAsync(app, inst));
        return results.OrderBy(r => r.Severity).ToList();
    }

    private static List<Issue> CheckLocal(AppServices app, Installation inst)
    {
        var results = new List<Issue>();
        CheckFolder(inst, results);
        CheckJava(app, inst, results);
        CheckMemory(app, inst, results);
        CheckDisk(inst, results);
        CheckLoaderAndFolders(app, inst, results);
        return results;
    }

    private static Issue Found(IssueSeverity severity, string title, string description, string? fixText = null,
        Func<WorkProgress, Task<string>>? fix = null) => new()
    {
        Severity = severity,
        Title = title,
        Description = description,
        FixText = fixText,
        Fix = fix,
        Selected = severity == IssueSeverity.Error
    };

    private static void CheckFolder(Installation inst, List<Issue> results)
    {
        try
        {
            Directory.CreateDirectory(inst.GameDir);
            var probe = inst.GameFile(".axoclient-schreibtest");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            results.Add(Found(IssueSeverity.Error, "Spielordner nicht beschreibbar",
                $"In \"{inst.GameDir}\" kann nichts gespeichert werden ({ErrorReport.Short(ex)}). Prüfe Virenscanner, " +
                "OneDrive-Synchronisierung und die Rechte auf den Ordner."));
        }
    }

    private static void CheckJava(AppServices app, Installation inst, List<Issue> results)
    {
        var chosen = inst.JavaPath ?? app.Settings.JavaPath;
        if (string.IsNullOrEmpty(chosen))
            return;

        var required = JavaRuntimes.RequiredMajor(inst);
        var where = inst.JavaPath is { Length: > 0 } ? "für diese Instanz" : "in den Einstellungen";
        const string toAutomaticText = "Auf \"Automatisch\" zurückstellen";
        var toAutomatic = Issue.Do(() =>
        {
            if (inst.JavaPath is { Length: > 0 })
                inst.JavaPath = null;
            else
                app.Settings.JavaPath = null;
            app.Instances.NotifyChanged();
        }, "Java steht wieder auf \"Automatisch\"; der Launcher holt die passende Laufzeit.");

        if (!File.Exists(chosen))
        {
            results.Add(Found(IssueSeverity.Error, "Eingestelltes Java gibt es nicht mehr",
                $"Der Pfad {where} zeigt auf \"{chosen}\", dort liegt aber keine Datei.", toAutomaticText, toAutomatic));
            return;
        }

        var major = JavaRuntimes.ReadMajor(chosen);
        if (major == 0)
            results.Add(Found(IssueSeverity.Info, "Java-Version unbekannt",
                $"Die Version von \"{chosen}\" konnte nicht gelesen werden. Minecraft {inst.MinecraftVersion} " +
                $"braucht Java {required}."));
        else if (major < required)
            results.Add(Found(IssueSeverity.Error, $"Java {major} ist zu alt",
                $"Minecraft {inst.MinecraftVersion} braucht mindestens Java {required}. Mit \"Automatisch\" " +
                "lädt der Launcher die passende Laufzeit selbst.", toAutomaticText, toAutomatic));
        else if (major > required + 4)
            results.Add(Found(IssueSeverity.Warning, $"Java {major} ist deutlich neuer als nötig",
                $"Minecraft {inst.MinecraftVersion} ist für Java {required} gemacht. Meist geht es trotzdem, " +
                "aber bei Abstürzen ist \"Automatisch\" die sichere Wahl.", toAutomaticText, toAutomatic));
    }

    private static void CheckMemory(AppServices app, Installation inst, List<Issue> results)
    {
        var mb = RamAdvisor.CurrentMb(inst, app.Settings);
        var total = RamAdvisor.TotalMb();
        var advice = RamAdvisor.Recommend(inst);

        var useAdviceText = $"Auf {Formats.Megabytes(advice.Mb)} stellen";
        var useAdvice = Issue.Do(() =>
        {
            inst.MaxRamMb = RamAdvisor.Recommend(inst).Mb;
            app.Instances.NotifyChanged();
        }, $"Arbeitsspeicher dieser Instanz steht jetzt auf {Formats.Megabytes(advice.Mb)}.");

        if (mb < 1024)
            results.Add(Found(IssueSeverity.Error, "Zu wenig Arbeitsspeicher eingestellt",
                $"{Formats.Megabytes(mb)} reichen für Minecraft nicht.", useAdviceText, useAdvice));
        else if (total > 0 && mb > total * 8 / 10)
            results.Add(Found(IssueSeverity.Error, "Mehr Arbeitsspeicher als vorhanden",
                $"Eingestellt sind {Formats.Megabytes(mb)}, der Rechner hat insgesamt {Formats.Megabytes(total)}. " +
                "Java startet dann gar nicht erst oder Windows wird sehr langsam.", useAdviceText, useAdvice));
        else if (mb < advice.Mb * 2 / 3)
            results.Add(Found(IssueSeverity.Warning, "Arbeitsspeicher knapp",
                $"Eingestellt sind {Formats.Megabytes(mb)}, empfohlen wären {Formats.Megabytes(advice.Mb)} " +
                $"({advice.Reason}). Das Spiel kann ruckeln oder mit \"Out of memory\" abstürzen.", useAdviceText, useAdvice));

        var preset = JvmPresets.EffectiveFor(inst, app.Settings);
        var suggested = JvmPresets.Get(JvmPresets.SuggestFor(mb));
        if (preset.Id != JvmPresets.CustomId && preset.Id != suggested.Id)
            results.Add(Found(IssueSeverity.Info, "Anderes Java-Preset könnte besser passen",
                $"Eingestellt ist \"{preset.Name}\", zu {Formats.Megabytes(mb)} passt eher \"{suggested.Name}\".",
                $"\"{suggested.Name}\" für diese Instanz nehmen",
                Issue.Do(() =>
                {
                    inst.JvmPreset = suggested.Id;
                    app.Instances.NotifyChanged();
                }, $"Preset \"{suggested.Name}\" gilt jetzt für \"{inst.Name}\".")));
    }

    private static void CheckDisk(Installation inst, List<Issue> results)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(inst.GameDir));
            if (root == null)
                return;
            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free < 1L << 30)
                results.Add(Found(IssueSeverity.Error, "Fast kein Speicherplatz frei",
                    $"Auf {root} sind nur noch {free / (1024 * 1024)} MB frei. Minecraft kann Welten dann nicht " +
                    "mehr speichern, was zu beschädigten Welten führt."));
            else if (free < 3L << 30)
                results.Add(Found(IssueSeverity.Warning, "Wenig Speicherplatz frei",
                    $"Auf {root} sind noch {free / (1024 * 1024 * 1024.0):0.#} GB frei. Für Spieldateien, " +
                    "Welten und Backups sollten es ein paar GB mehr sein."));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
        }
    }

    private static void CheckLoaderAndFolders(AppServices app, Installation inst, List<Issue> results)
    {
        var mods = FileOps.Files(inst.ModsDir, "*.jar");
        if (!inst.CanUseMods && mods.Count > 0)
            results.Add(Found(IssueSeverity.Warning, $"{mods.Count} Mod(s) ohne Mod-Loader",
                "Diese Instanz läuft als Vanilla, deshalb wird der Ordner \"mods\" gar nicht gelesen. " +
                "Stelle die Instanz auf Fabric oder Forge um, damit die Mods geladen werden."));

        var duplicates = mods
            .Where(m => !BadgeMod.IsModFile(m))
            .Select(m => (Path: m, Stem: ModStem(Path.GetFileNameWithoutExtension(m))))
            .Where(m => m.Stem.Length >= 3)
            .GroupBy(m => m.Stem, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Take(5);
        foreach (var group in duplicates)
        {
            var older = group.Select(g => g.Path).OrderByDescending(File.GetLastWriteTimeUtc).Skip(1).ToList();
            results.Add(Found(IssueSeverity.Error, $"Mod doppelt: {group.Key}",
                "Es liegen mehrere Dateien desselben Mods im Ordner \"mods\": " +
                string.Join(", ", group.Select(g => Path.GetFileNameWithoutExtension(g.Path))) +
                ". Der Loader bricht meist mit \"duplicate mods\" ab.",
                $"Ältere Datei{(older.Count > 1 ? "en" : "")} in den Papierkorb",
                async _ =>
                {
                    await Task.Run(() => older.ForEach(FileOps.Recycle));
                    return $"{older.Count} ältere Datei(en) von {group.Key} in den Papierkorb verschoben.";
                }));
        }

        var shaders = FileOps.CountEntries(inst.ContentDir(ContentType.Shader));
        if (shaders > 0 && inst.CanUseMods && !mods.Any(m => ContainsAny(m, "iris", "oculus", "optifine")))
        {
            var (slug, name) = inst.Loader == LoaderType.Forge ? ("oculus", "Oculus") : ("iris", "Iris");
            results.Add(Found(IssueSeverity.Warning, "Shader ohne Iris",
                $"Es liegen {shaders} Shaderpaket(e) in der Instanz, aber kein Mod, der sie anzeigen kann.",
                $"{name} installieren",
                Issue.Do(progress => app.ContentOf(inst).InstallBySlugAsync(slug, ContentType.Mod, progress.Text),
                    $"{name} wurde installiert; Shader lassen sich jetzt im Spiel unter den Grafikeinstellungen wählen.")));
        }
    }

    private static async Task<List<Issue>> CheckModsOnlineAsync(AppServices app, Installation inst)
    {
        var results = new List<Issue>();
        if (!inst.CanUseMods)
            return results;

        var files = FileOps.Files(inst.ModsDir, "*.jar");
        if (files.Count == 0 || files.Count > MaxHashedMods)
            return results;

        Dictionary<string, ContentVersion> found;
        Dictionary<string, string> hashes;
        try
        {
            using var timeout = new CancellationTokenSource(OnlineTimeout);
            hashes = await Task.Run(() => files.ToDictionary(f => f, FileOps.Sha1), timeout.Token);
            found = await app.Modrinth.LookupByHashAsync(hashes.Values).WaitAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException
                                       or UnauthorizedAccessException)
        {
            return results;
        }

        var loaderName = inst.Loader.ModrinthName();
        var installedProjects = found.Values.Select(v => v.ProjectId).ToHashSet();
        var wrongVersion = new List<string>();
        var wrongLoader = new List<string>();
        var missing = new Dictionary<string, string>();

        foreach (var (file, hash) in hashes)
        {
            if (!found.TryGetValue(hash, out var version))
                continue;
            var name = Path.GetFileName(file);
            if (version.GameVersions.Count > 0 && !version.GameVersions.Contains(inst.MinecraftVersion))
                wrongVersion.Add($"{name} (für {string.Join(", ", version.GameVersions.TakeLast(3))})");
            else if (version.Loaders.Count > 0 && !version.Loaders.Contains(loaderName))
                wrongLoader.Add($"{name} (für {string.Join(", ", version.Loaders)})");

            foreach (var needed in version.RequiredProjectIds.Where(id => !installedProjects.Contains(id)))
                missing.TryAdd(needed, name);
        }

        if (wrongVersion.Count > 0)
            results.Add(Found(IssueSeverity.Error, $"{wrongVersion.Count} Mod(s) sind nicht für Minecraft {inst.MinecraftVersion}",
                Formats.Some(wrongVersion, MaxListed) +
                ". Das Spiel bricht damit meist beim Laden ab. Unter \"Mods\" kannst du nach Updates suchen " +
                "oder die Instanz auf eine passende Version hochziehen."));

        if (wrongLoader.Count > 0)
            results.Add(Found(IssueSeverity.Error, $"{wrongLoader.Count} Mod(s) sind für einen anderen Mod-Loader",
                Formats.Some(wrongLoader, MaxListed) + $". Diese Instanz nutzt {inst.Loader}."));

        if (missing.Count > 0)
            results.Add(await MissingDependenciesAsync(app, inst, missing));

        return results;
    }

    private static async Task<Issue> MissingDependenciesAsync(AppServices app, Installation inst,
        Dictionary<string, string> missing)
    {
        Dictionary<string, ProjectSummary> projects;
        try
        {
            using var timeout = new CancellationTokenSource(OnlineTimeout);
            projects = await app.Modrinth.GetProjectsAsync(missing.Keys).WaitAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            projects = [];
        }

        string TitleOf(string id) => projects.TryGetValue(id, out var p) && p.Title.Length > 0 ? p.Title : id;

        var wanted = missing.Keys.ToDictionary(id => id, TitleOf);
        return Found(IssueSeverity.Warning,
            missing.Count == 1 ? "Ein benötigter Mod fehlt" : $"{missing.Count} benötigte Mods fehlen",
            Formats.Some(missing.Select(m => $"{TitleOf(m.Key)} (für {m.Value})").ToList(), MaxListed) +
            ". Ohne sie starten die betroffenen Mods nicht.",
            missing.Count == 1 ? "Fehlenden Mod installieren" : $"{missing.Count} Mods installieren",
            progress => InstallMissingAsync(app.ContentOf(inst), wanted, progress));
    }

    private static async Task<string> InstallMissingAsync(ContentStore store, Dictionary<string, string> wanted,
        WorkProgress progress)
    {
        var installed = 0;
        var failed = new List<string>();
        foreach (var (projectId, title) in wanted)
        {
            progress.Cancel.ThrowIfCancellationRequested();
            try
            {
                await store.InstallAsync(projectId, title, ContentType.Mod, progress.Text);
                installed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed.Add($"{title} ({ErrorReport.Short(ex)})");
            }
        }

        return failed.Count == 0
            ? $"{installed} fehlende(r) Mod(s) installiert."
            : $"{installed} von {wanted.Count} installiert. Nicht geklappt hat: {string.Join(", ", failed)}";
    }

    private static string ModStem(string fileName)
    {
        var cut = Regex.Match(fileName, @"^(.*?)[-_+]v?\d");
        return (cut.Success ? cut.Groups[1].Value : fileName).Trim('-', '_', '+', ' ');
    }

    private static bool ContainsAny(string path, params string[] parts) =>
        parts.Any(p => Path.GetFileName(path).Contains(p, StringComparison.OrdinalIgnoreCase));
}
