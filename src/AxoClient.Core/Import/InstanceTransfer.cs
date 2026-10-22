using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Import;

/// <summary>
/// Übernimmt Teile einer anderen Instanz in eine bestehende – aus AxoClient selbst (Id <c>axo:&lt;id&gt;</c>) oder aus einem
/// anderen Launcher (Id aus dem <see cref="ForeignScanner"/>). Nichts Vorhandenes geht verloren: Welten mit gleichem
/// Namen bekommen einen neuen Namen, Einstellungen werden vorher als <c>.bak</c> gesichert, Server nur ergänzt.
/// Passen Loader oder Version nicht, werden Modrinth-Mods der Quelle in der passenden Version neu installiert.
/// </summary>
public sealed class InstanceTransfer(ForeignScanner scanner, InstanceManager instances, ContentManager content, ServerListManager servers)
{
    public const string OwnPrefix = "axo:";

    private static readonly string[] OptionFiles = ["options.txt", "optionsof.txt", "optionsshaders.txt"];
    private static readonly string[] ShaderConfigFiles = ["config/iris.properties", "config/oculus.properties"];

    private sealed record Source(string Name, string GameDir, string? ModsDir, string? ConfigDir, LoaderType? Loader, string? Version,
        string? OwnId, string? ExtraDataDir);

    /// <summary>Andere AxoClient-Instanzen als Quelle (für die Auswahl im Dialog).</summary>
    public async Task<ForeignLauncherView> OwnSourcesAsync(string exceptId, CancellationToken ct)
    {
        var list = (await instances.ListAsync(ct)).Where(i => i.Id != exceptId).Select(i => new ForeignInstanceView(
            OwnPrefix + i.Id, i.Name, i.Loader, i.MinecraftVersion, i.LoaderVersion,
            WorldManager.Count(Path.Combine(instances.GameDir(i.Id), "saves")), i.ModCount, false, 0, null, null)).ToList();
        return new ForeignLauncherView("axoclient", "AxoClient", true, list, null);
    }

    public async Task<ForeignImportResult> TransferAsync(string sourceId, string targetId, ForeignImportParts parts, JobContext job)
    {
        var target = await instances.GetAsync(targetId, job.Token);
        var source = await ResolveAsync(sourceId, job.Token);
        var to = instances.GameDir(target.Id);
        var notes = new List<TextRef>();

        if (parts.Worlds)
        {
            job.Status("transfer.worlds");
            var count = await Task.Run(() => CopyWorlds(Path.Combine(source.GameDir, "saves"), Path.Combine(to, "saves")), job.Token);
            notes.Add(TextRef.Of("transfer.result.worlds", ("count", count)));
        }

        if (parts.Settings)
        {
            job.Status("transfer.settings");
            var copied = OptionFiles.Count(f => CopyWithBackup(Path.Combine(source.GameDir, f), Path.Combine(to, f)));
            notes.Add(new TextRef(copied > 0 ? "transfer.result.settings" : "transfer.result.noSettings"));
            var added = await MergeServersAsync(Path.Combine(source.GameDir, "servers.dat"), target.Id);
            if (added > 0)
                notes.Add(TextRef.Of("transfer.result.servers", ("count", added)));
        }

        if (parts.Packs)
        {
            job.Status("transfer.packs");
            var count = await Task.Run(() => CopyMissing(Path.Combine(source.GameDir, "resourcepacks"), Path.Combine(to, "resourcepacks"))
                                             + CopyMissing(Path.Combine(source.GameDir, "shaderpacks"), Path.Combine(to, "shaderpacks")), job.Token);
            foreach (var file in ShaderConfigFiles)
                CopyWithBackup(Path.Combine(source.GameDir, file), Path.Combine(to, file));
            notes.Add(TextRef.Of("transfer.result.packs", ("count", count)));
        }

        if (parts.Screenshots)
        {
            var count = await Task.Run(() => CopyMissing(Path.Combine(source.GameDir, "screenshots"), Path.Combine(to, "screenshots")), job.Token);
            notes.Add(TextRef.Of("transfer.result.screenshots", ("count", count)));
        }

        if (parts.Mods)
            await TransferModsAsync(source, target, job, notes);

        await content.SyncAsync(target.Id, job.Token);
        job.Report(1);
        return new ForeignImportResult(target.Name, target.Id, notes, null);
    }

    private async Task<Source> ResolveAsync(string id, CancellationToken ct)
    {
        if (id.StartsWith(OwnPrefix, StringComparison.Ordinal))
        {
            var own = await instances.GetAsync(Text.RequireId(id[OwnPrefix.Length..]), ct);
            var dir = instances.GameDir(own.Id);
            return new Source(own.Name, dir, Path.Combine(dir, "mods"), Path.Combine(dir, "config"), own.Loader.Type, own.Minecraft.Version, own.Id, null);
        }
        var foreign = scanner.Get(id) ?? throw new AxoException("import.problem.gone");
        if (foreign.Problem != null)
            throw new AxoException(foreign.Problem.Code);
        return new Source(foreign.Name, foreign.GameDir, foreign.ModsDir, foreign.ExtraConfigDir ?? Path.Combine(foreign.GameDir, "config"),
            foreign.Loader, foreign.MinecraftVersion, null, foreign.ExtraDataDir);
    }

    private async Task TransferModsAsync(Source source, Instance target, JobContext job, List<TextRef> notes)
    {
        if (!target.ModLock.CanAdd())
        {
            notes.Add(new TextRef("transfer.result.modsLocked"));
            return;
        }
        if (target.Loader.Type == LoaderType.Vanilla)
        {
            notes.Add(new TextRef("transfer.result.modsVanilla"));
            return;
        }
        if (source.ModsDir == null || !Directory.Exists(source.ModsDir))
        {
            notes.Add(new TextRef("transfer.result.noMods"));
            return;
        }
        var to = instances.GameDir(target.Id);
        var sameSetup = (source.Loader == null || source.Loader == target.Loader.Type)
                        && (source.Version == null || source.Version == target.Minecraft.Version);
        if (sameSetup)
        {
            job.Status("transfer.mods");
            var copied = await Task.Run(() => CopyMissing(source.ModsDir, Path.Combine(to, "mods"), ForeignInstance.IsModFile), job.Token);
            if (source.ConfigDir != null)
                await Task.Run(() => CopyMissing(source.ConfigDir, Path.Combine(to, "config")), job.Token);
            await content.SyncAsync(target.Id, job.Token);
            try
            {
                await content.IdentifyLocalAsync(target.Id, job.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or AxoException)
            {
                AppLog.Warn("Übernommene Mods konnten nicht zugeordnet werden", ex);
            }
            notes.Add(TextRef.Of("transfer.result.modsCopied", ("count", copied)));
            return;
        }

        // Andere Version oder anderer Loader: nur Mods mit bekannter Modrinth-Herkunft lassen sich passend neu holen
        if (source.OwnId == null)
        {
            notes.Add(TextRef.Of("transfer.result.modsMismatch", ("loader", source.Loader?.ToString() ?? "?"), ("version", source.Version ?? "?")));
            return;
        }
        var doc = await instances.ContentOf(source.OwnId).LoadAsync(job.Token);
        var installed = (await content.SyncAsync(target.Id, job.Token)).Mods.Where(m => m.ProjectId != null).Select(m => m.ProjectId!).ToHashSet();
        var known = doc.Mods.Where(m => m.Source == ContentSource.Modrinth && m.ProjectId != null && !installed.Contains(m.ProjectId)).ToList();
        var unknown = doc.Mods.Count(m => m.Source is ContentSource.Local or ContentSource.Friend);
        var failed = 0;
        for (var i = 0; i < known.Count; i++)
        {
            job.Token.ThrowIfCancellationRequested();
            job.Status("transfer.modsInstalling", ("index", i + 1), ("count", known.Count));
            try
            {
                var files = await content.InstallAsync(target.Id, ContentType.Mod, new InstallRequest("modrinth", known[i].ProjectId!),
                    job.Sub((double)i / known.Count, (double)(i + 1) / known.Count));
                if (!known[i].Enabled && files.Count > 0)
                    await content.SetEnabledAsync(target.Id, ContentType.Mod, files[0], false, job.Token);
            }
            catch (Exception ex) when (ex is AxoException or HttpRequestException)
            {
                failed++;
            }
        }
        notes.Add(TextRef.Of("transfer.result.modsReinstalled", ("count", known.Count - failed), ("version", target.Minecraft.Version)));
        if (failed > 0)
            notes.Add(TextRef.Of("transfer.result.modsUnavailable", ("count", failed)));
        if (unknown > 0)
            notes.Add(TextRef.Of("transfer.result.modsLocal", ("count", unknown)));
    }

    private async Task<int> MergeServersAsync(string serversDat, string targetId)
    {
        if (!File.Exists(serversDat))
            return 0;
        NbtCompound root;
        try
        {
            root = Nbt.ReadFile(serversDat);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            return 0;
        }
        if (!root.TryGetValue("servers", out var value) || value is not NbtList list)
            return 0;
        var known = servers.NormalizedAddresses(targetId).ToHashSet();
        var added = 0;
        foreach (var entry in list.OfType<NbtCompound>())
        {
            var address = Text.Clean(entry.Get<string>("ip"), 255);
            if (address.Length == 0 || !ServerPing.IsValidAddress(address) || !known.Add(ServerPing.Normalize(address)))
                continue;
            var name = Text.Clean(entry.Get<string>("name"), 80);
            await servers.AddAsync(targetId, name.Length > 0 ? name : address, address);
            added++;
        }
        return added;
    }

    /// <summary>Kopiert alle Welten; gibt es den Ordnernamen schon, bekommt die Kopie " (2)", " (3)" … angehängt.</summary>
    private static int CopyWorlds(string from, string to)
    {
        if (!Directory.Exists(from))
            return 0;
        Directory.CreateDirectory(to);
        var count = 0;
        foreach (var world in Directory.EnumerateDirectories(from).Where(d => File.Exists(Path.Combine(d, "level.dat"))))
        {
            var name = Path.GetFileName(world);
            var destination = Path.Combine(to, name);
            for (var n = 2; Directory.Exists(destination); n++)
                destination = Path.Combine(to, $"{name} ({n})");
            CopyDirectory(world, destination);
            count++;
        }
        return count;
    }

    private static bool CopyWithBackup(string from, string to)
    {
        if (!File.Exists(from))
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (File.Exists(to))
            File.Copy(to, to + ".bak", overwrite: true);
        using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(to, FileMode.Create, FileAccess.Write);
        input.CopyTo(output);
        return true;
    }

    /// <summary>Kopiert Dateien und Ordner, die im Ziel noch fehlen. Vorhandenes bleibt unangetastet.</summary>
    private static int CopyMissing(string from, string to, Func<string, bool>? fileFilter = null)
    {
        if (!Directory.Exists(from))
            return 0;
        Directory.CreateDirectory(to);
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(from))
        {
            var destination = Path.Combine(to, Path.GetFileName(file));
            if ((fileFilter != null && !fileFilter(file)) || File.Exists(destination) || Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                continue;
            if (TryCopy(file, destination))
                count++;
        }
        if (fileFilter != null)
            return count;
        foreach (var dir in Directory.EnumerateDirectories(from).Where(d => new DirectoryInfo(d).LinkTarget == null))
        {
            var destination = Path.Combine(to, Path.GetFileName(dir));
            if (Directory.Exists(destination))
                continue;
            CopyDirectory(dir, destination);
            count++;
        }
        return count;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            if (!Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                TryCopy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(from).Where(d => new DirectoryInfo(d).LinkTarget == null))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    private static bool TryCopy(string from, string to)
    {
        try
        {
            using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write);
            input.CopyTo(output);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Beim Übernehmen nicht kopierbar: {Path.GetFileName(from)}", ex);
            return false;
        }
    }
}
