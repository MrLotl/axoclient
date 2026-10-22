using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Import;

/// <summary>
/// Legt aus ausgewählten fremden Instanzen neue AxoClient-Instanzen an und kopiert, was gewählt wurde.
/// Der andere Launcher bleibt unberührt. Mods kommen nur mit, wenn Loader und Version passen; erkannte Jars werden
/// danach bei Modrinth zugeordnet, damit Updates und Abhängigkeitsprüfung funktionieren.
/// </summary>
public sealed class ForeignImporter(ForeignScanner scanner, InstanceManager instances, ContentManager content)
{
    // Nie kopieren: Laufzeitdaten, Caches und Dateien des fremden Launchers
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs", "crash-reports", ".cache", "cache", "natives", "libraries", "versions", "assets", "runtime", "debug",
        ".fabric", ".mixin.out", "downloads", "profile.json", "minecraftinstance.json", "session.lock", "usercache.json",
        "usernamecache.json", "launcher_log.txt", "launcher_profiles.json", "launcher_accounts.json", "launcher_msa_credentials.bin",
        "instance.cfg", "mmc-pack.json", "icon.png", "manifest.json", "modlist.html"
    };

    // Aus geteilten Ordnern (.minecraft) nur das hier – der Rest gehört anderen Profilen
    private static readonly string[] SharedItems =
    [
        "saves", "resourcepacks", "shaderpacks", "screenshots", "options.txt", "optionsof.txt", "optionsshaders.txt", "servers.dat"
    ];

    public async Task<IReadOnlyList<ForeignImportResult>> ImportAsync(IReadOnlyList<ForeignImportChoice> choices, JobContext job)
    {
        var results = new List<ForeignImportResult>();
        for (var i = 0; i < choices.Count; i++)
        {
            job.Token.ThrowIfCancellationRequested();
            var choice = choices[i];
            job.Status("import.importing", ("name", choice.Name), ("index", i + 1), ("count", choices.Count));
            try
            {
                results.Add(await ImportOneAsync(choice, job.Sub((double)i / choices.Count, (double)(i + 1) / choices.Count)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Import von \"{choice.Name}\" fehlgeschlagen", ex);
                results.Add(new ForeignImportResult(choice.Name, null, [],
                    ex is AxoException axo ? axo.Text : TextRef.Of("errors.unexpected", ("detail", ex.GetType().Name))));
            }
        }
        job.Report(1);
        return results;
    }

    private async Task<ForeignImportResult> ImportOneAsync(ForeignImportChoice choice, JobContext job)
    {
        var source = scanner.Get(choice.Id) ?? throw new AxoException("import.problem.gone");
        if (source.Problem != null)
            throw new AxoException(source.Problem.Code);
        var version = choice.MinecraftVersion.Trim();
        var sameSetup = source.Loader == choice.Loader && source.MinecraftVersion == version;
        var loaderVersion = sameSetup && choice.Loader != LoaderType.Vanilla && LoaderManager.IsSafeVersion(source.LoaderVersion)
            ? source.LoaderVersion
            : null;
        var modsFit = choice.Parts.Mods && choice.Loader != LoaderType.Vanilla
                      && (source.Loader == null || source.Loader == choice.Loader)
                      && (source.MinecraftVersion == null || source.MinecraftVersion == version);

        var instance = await instances.CreateAsync(new CreateInstanceRequest(choice.Name, version, choice.Loader, loaderVersion),
            OriginType.Import, job.Token);
        var notes = new List<TextRef>();
        try
        {
            var target = instances.GameDir(instance.Id);
            var parts = choice.Parts with { Mods = modsFit };
            job.Status("import.copying", ("name", instance.Name));
            var failed = await Task.Run(() => Copy(source, target, parts, job.Sub(0, 0.7)), job.Token);
            if (failed > 0)
                notes.Add(TextRef.Of("import.result.copyFailed", ("count", failed)));
        }
        catch
        {
            await instances.DeleteAsync(instance.Id);
            throw;
        }

        await content.SyncAsync(instance.Id, job.Token);
        if (modsFit && source.Mods > 0)
        {
            job.Status("import.identifying");
            try
            {
                await content.IdentifyLocalAsync(instance.Id, job.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or AxoException or TaskCanceledException && !job.Token.IsCancellationRequested)
            {
                // Ohne Internet bleiben die Mods einfach "lokal"
                AppLog.Warn("Importierte Mods konnten nicht zugeordnet werden", ex);
            }
        }
        if (modsFit && source.ModrinthMods.Count > 0)
            await InstallModrinthModsAsync(instance.Id, source.ModrinthMods, job.Sub(0.7, 1), notes);

        if (choice.Parts.Mods && !modsFit && source.Mods > 0)
            notes.Add(choice.Loader == LoaderType.Vanilla
                ? TextRef.Of("import.result.modsVanilla", ("count", source.Mods))
                : TextRef.Of("import.result.modsMismatch", ("count", source.Mods),
                    ("loader", source.Loader?.ToString() ?? "?"), ("version", source.MinecraftVersion ?? "?")));
        return new ForeignImportResult(instance.Name, instance.Id, notes, null);
    }

    private async Task InstallModrinthModsAsync(string instanceId, List<ForeignModrinthMod> mods, JobContext job, List<TextRef> notes)
    {
        var failed = new List<string>();
        for (var i = 0; i < mods.Count; i++)
        {
            job.Token.ThrowIfCancellationRequested();
            var mod = mods[i];
            job.Status("import.downloadingMods", ("index", i + 1), ("count", mods.Count));
            try
            {
                var files = await content.InstallAsync(instanceId, ContentType.Mod, new InstallRequest("modrinth", mod.ProjectId, mod.VersionId),
                    job.Sub((double)i / mods.Count, (double)(i + 1) / mods.Count));
                if (!mod.Enabled && files.Count > 0)
                    await content.SetEnabledAsync(instanceId, ContentType.Mod, files[0], false, job.Token);
            }
            catch (Exception ex) when (ex is AxoException or HttpRequestException)
            {
                failed.Add(mod.ProjectId);
            }
        }
        if (failed.Count > 0)
            notes.Add(TextRef.Of("import.result.modrinthFailed", ("count", failed.Count)));
    }

    private static string? Category(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower switch
        {
            "saves" => "worlds",
            "resourcepacks" or "shaderpacks" or "texturepacks" => "packs",
            "screenshots" => "screenshots",
            "servers.dat" or "servers.dat_old" => "settings",
            _ when lower.StartsWith("options", StringComparison.Ordinal) && lower.EndsWith(".txt", StringComparison.Ordinal) => "settings",
            // alles andere (mods, config, Mod-Daten wie Minimap-Wegpunkte) gehört zu den Mods
            _ => "mods"
        };
    }

    private static bool Wanted(string name, ForeignImportParts parts) => Category(name) switch
    {
        "worlds" => parts.Worlds,
        "packs" => parts.Packs,
        "screenshots" => parts.Screenshots,
        "settings" => parts.Settings,
        _ => parts.Mods
    };

    private static int Copy(ForeignInstance source, string target, ForeignImportParts parts, JobContext job)
    {
        var failed = 0;
        var modsDir = source.ModsDir;
        if (source.Shared)
        {
            var items = SharedItems.Where(item => Wanted(item, parts)).ToList();
            foreach (var dir in new[] { source.ExtraDataDir, source.GameDir }.OfType<string>())
            foreach (var item in items)
                failed += CopyEntry(Path.Combine(dir, item), Path.Combine(target, item), job);
            if (parts.Mods && modsDir != null)
            {
                failed += source.ExtraModsDir != null
                    ? CopyModFiles(modsDir, Path.Combine(target, "mods"))
                    : CopyEntry(modsDir, Path.Combine(target, "mods"), job);
                failed += CopyEntry(source.ExtraConfigDir ?? Path.Combine(source.GameDir, "config"), Path.Combine(target, "config"), job);
            }
            return failed;
        }

        var entries = Directory.EnumerateFileSystemEntries(source.GameDir).ToList();
        for (var i = 0; i < entries.Count; i++)
        {
            var name = Path.GetFileName(entries[i]);
            job.Report((double)i / Math.Max(1, entries.Count));
            if (Skipped.Contains(name) || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || !Wanted(name, parts)
                || (name.Equals("mods", StringComparison.OrdinalIgnoreCase) && !source.GameDirMods))
                continue;
            failed += CopyEntry(entries[i], Path.Combine(target, name), job);
        }
        if (parts.Mods && source.ExtraModsDir != null)
        {
            failed += CopyModFiles(source.ExtraModsDir, Path.Combine(target, "mods"));
            if (source.ExtraConfigDir != null)
                failed += CopyEntry(source.ExtraConfigDir, Path.Combine(target, "config"), job);
        }
        return failed;
    }

    private static int CopyModFiles(string from, string to)
    {
        if (!Directory.Exists(from))
            return 0;
        Directory.CreateDirectory(to);
        return Directory.EnumerateFiles(from).Where(ForeignInstance.IsModFile)
            .Count(file => !CopyFile(file, Path.Combine(to, Path.GetFileName(file))));
    }

    private static int CopyEntry(string from, string to, JobContext job)
    {
        job.Token.ThrowIfCancellationRequested();
        if (File.Exists(from))
            return CopyFile(from, to) ? 0 : 1;
        if (!Directory.Exists(from))
            return 0;
        var failed = 0;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            if (!Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase) && !CopyFile(file, Path.Combine(to, Path.GetFileName(file))))
                failed++;
        // Verknüpfungen (Junctions) nicht verfolgen – sonst landet womöglich ein ganz anderer Ordner in der Instanz
        foreach (var dir in Directory.EnumerateDirectories(from).Where(d => new DirectoryInfo(d).LinkTarget == null))
            failed += CopyEntry(dir, Path.Combine(to, Path.GetFileName(dir)), job);
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
            AppLog.Warn($"Beim Import nicht kopierbar: {Path.GetFileName(from)}", ex);
            return false;
        }
    }
}
