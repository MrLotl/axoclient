namespace AxoClient.Import;

public sealed record ForeignImportChoice(ForeignInstance Source, string Name, LoaderType Loader, string MinecraftVersion);

public class ForeignImporter(AppServices app)
{
    private const int MaxListed = 8;

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs", "crash-reports", ".cache", "cache", "natives", "libraries", "versions", "assets", "runtime", "debug",
        ".fabric", ".mixin.out", "downloads", "profile.json", "minecraftinstance.json",
        "session.lock", "usercache.json", "usernamecache.json", "launcher_log.txt"
    };

    private static readonly string[] SharedItems =
    [
        "saves", "resourcepacks", "shaderpacks", "screenshots", "options.txt", "optionsof.txt", "optionsshaders.txt",
        "servers.dat"
    ];

    public async Task<List<string>> ImportAsync(IReadOnlyList<ForeignImportChoice> choices, WorkProgress progress)
    {
        var report = new List<string>();
        for (var i = 0; i < choices.Count; i++)
        {
            progress.Cancel.ThrowIfCancellationRequested();
            var choice = choices[i];
            progress.Text.Report($"Übernehme \"{choice.Name}\" ({i + 1} von {choices.Count})...");
            progress.Fraction.Report((double)i / choices.Count);
            try
            {
                report.AddRange(await ImportOneAsync(choice, progress));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorReport.Log($"Instanz \"{choice.Name}\" aus {choice.Source.Launcher} übernehmen", ex);
                report.Add($"\"{choice.Name}\" ({choice.Source.Launcher}): nicht übernommen – {ErrorReport.Short(ex)}");
            }
        }
        progress.Fraction.Report(1);
        return report;
    }

    private async Task<List<string>> ImportOneAsync(ForeignImportChoice choice, WorkProgress progress)
    {
        var version = choice.MinecraftVersion.Trim();
        await CheckVersionAsync(choice.Loader, version);

        var source = choice.Source;
        var sameSetup = ForeignInstance.AsLoaderType(source.Loader) == choice.Loader && source.MinecraftVersion == version;
        var loaderVersion = sameSetup && choice.Loader != LoaderType.Vanilla
                                      && GameInstaller.IsSafeLoaderVersion(source.LoaderVersion)
            ? source.LoaderVersion
            : null;
        var modsFit = choice.Loader != LoaderType.Vanilla
                      && (source.Loader == ForeignLoader.Unknown || ForeignInstance.AsLoaderType(source.Loader) == choice.Loader)
                      && (source.MinecraftVersion == null || source.MinecraftVersion == version);

        var failed = 0;
        var inst = await app.Instances.CreateAsync(choice.Name.Trim(), choice.Loader, version, loaderVersion,
            async created => failed = await Task.Run(() => Copy(source, created.GameDir, modsFit, progress)));

        var what = new List<string>();
        if (source.Worlds > 0)
            what.Add(Formats.Count(source.Worlds, "Welt", "Welten"));
        if (modsFit && source.Mods > 0)
            what.Add(Formats.Count(source.Mods, "Mod", "Mods"));
        var lines = new List<string>
        {
            $"\"{inst.Name}\" aus {source.Launcher} übernommen ({inst.Description}" +
            (what.Count > 0 ? ", " + string.Join(", ", what) : "") + ")."
        };

        if (modsFit && source.ModrinthMods.Count > 0)
        {
            try
            {
                await InstallModrinthModsAsync(inst, source.ModrinthMods, progress, lines);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorReport.Log("Mods von Modrinth für eine übernommene Instanz laden", ex);
                lines.Add($"  Die Mods von Modrinth konnten nicht geladen werden ({ErrorReport.Short(ex)}). " +
                          "Über die Mod-Suche lassen sie sich nachinstallieren.");
            }
        }
        if (!modsFit && source.Mods > 0)
            lines.Add(!inst.CanUseMods
                ? $"  Die {source.Mods} Mods wurden nicht übernommen, weil die neue Instanz Vanilla ist."
                : $"  Die {source.Mods} Mods wurden nicht übernommen, weil sie für {source.Loader} " +
                  $"{source.MinecraftVersion} gedacht sind. Über \"Übertragen\" oder die Mod-Suche lassen sie sich neu holen.");
        if (failed > 0)
            lines.Add($"  {failed} Dateien ließen sich nicht kopieren (vermutlich gerade von einem Spiel geöffnet).");
        return lines;
    }

    private async Task InstallModrinthModsAsync(Installation inst, List<ForeignModrinthMod> mods, WorkProgress progress,
        List<string> lines)
    {
        progress.Text.Report($"Suche {mods.Count} Mods bei Modrinth...");
        var projects = await app.Modrinth.GetProjectsAsync(mods.Select(m => m.ProjectId));
        var wanted = mods.Select(mod => new ContentStore.PinnedContent(ContentType.Mod, mod.ProjectId, mod.VersionId,
                projects.TryGetValue(mod.ProjectId, out var project) && project.Title.Length > 0 ? project.Title : mod.ProjectId,
                mod.Enabled))
            .ToList();

        var result = await app.ContentOf(inst).InstallPinnedAsync(wanted, progress);
        if (result.Failures.Count > 0)
            lines.Add("  Von Modrinth nicht geladen: " + Formats.Some(result.Failures, MaxListed, "; ") + ".");
    }

    private async Task CheckVersionAsync(LoaderType loader, string version)
    {
        try
        {
            await VersionCatalog.EnsureAvailableAsync(app.Http, loader, version, "die Instanz wird nicht übernommen");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            ErrorReport.Log("Versionsliste für den Import laden", ex);
        }
    }

    private static int Copy(ForeignInstance source, string target, bool withMods, WorkProgress progress)
    {
        var failed = 0;
        var modsDir = source.ModsDir;
        if (source.Shared)
        {
            foreach (var dir in new[] { source.ExtraDataDir, source.GameDir }.OfType<string>())
                foreach (var item in SharedItems)
                    failed += CopyEntry(Path.Combine(dir, item), Path.Combine(target, item), progress);
            if (withMods && modsDir != null)
            {
                failed += source.ExtraModsDir != null
                    ? CopyModFiles(modsDir, Path.Combine(target, "mods"))
                    : CopyEntry(modsDir, Path.Combine(target, "mods"), progress);
                failed += CopyEntry(source.ExtraConfigDir ?? Path.Combine(source.GameDir, "config"),
                    Path.Combine(target, "config"), progress);
            }
            return failed;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(source.GameDir))
        {
            var name = Path.GetFileName(entry);
            if (Skipped.Contains(name) || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                || (name.Equals("mods", StringComparison.OrdinalIgnoreCase) && (!withMods || !source.GameDirMods)))
                continue;
            failed += CopyEntry(entry, Path.Combine(target, name), progress);
        }
        if (withMods && source.ExtraModsDir != null)
        {
            failed += CopyModFiles(source.ExtraModsDir, Path.Combine(target, "mods"));
            if (source.ExtraConfigDir != null)
                failed += CopyEntry(source.ExtraConfigDir, Path.Combine(target, "config"), progress);
        }
        return failed;
    }

    private static int CopyModFiles(string from, string to)
    {
        Directory.CreateDirectory(to);
        return Directory.EnumerateFiles(from).Where(ForeignInstance.IsModFile)
            .Count(file => !CopyFile(file, Path.Combine(to, Path.GetFileName(file))));
    }

    private static int CopyEntry(string from, string to, WorkProgress progress)
    {
        progress.Cancel.ThrowIfCancellationRequested();
        if (File.Exists(from))
            return CopyFile(from, to) ? 0 : 1;
        if (!Directory.Exists(from))
            return 0;
        var failed = 0;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            if (!Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase)
                && !CopyFile(file, Path.Combine(to, Path.GetFileName(file))))
                failed++;
        }
        foreach (var dir in Directory.EnumerateDirectories(from).Where(d => new DirectoryInfo(d).LinkTarget == null))
            failed += CopyEntry(dir, Path.Combine(to, Path.GetFileName(dir)), progress);
        return failed;
    }

    private static bool CopyFile(string from, string to)
    {
        try
        {
            if (File.Exists(to))
                return true;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write);
            input.CopyTo(output);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorReport.Log($"\"{from}\" kopieren", ex);
            return false;
        }
    }
}
