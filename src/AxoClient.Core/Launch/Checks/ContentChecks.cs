using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;

namespace AxoClient.Core.Launch.Checks;

/// <summary>
/// Abgleich mit <c>content.json</c>: fehlende oder beschädigte Dateien mit bekannter Quelle werden still neu geladen,
/// lokale Dateien ohne Quelle nur gemeldet. Hashes nur, wenn sich Größe oder Änderungszeit geändert haben.
/// </summary>
public sealed class ContentFilesCheck(ContentProviders providers, DownloadManager downloads, ContentManager content) : ILaunchCheck
{
    public string Id => "contentFiles";
    public LaunchPhase Phase => LaunchPhase.Content;

    public Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        var results = new List<CheckResult>();
        var redownload = new List<(ContentType Type, ContentEntry Entry)>();
        foreach (var type in Enum.GetValues<ContentType>())
        foreach (var entry in ctx.Content.ListOf(type))
        {
            if (entry.Source == ContentSource.AxoClient)
                continue;
            var path = Path.Combine(ctx.GameDir, ContentFolders.Of(type), entry.DiskName);
            if (!File.Exists(path))
            {
                if (Directory.Exists(Path.Combine(ctx.GameDir, ContentFolders.Of(type), entry.File)))
                    continue;
                if (HasSource(entry))
                    redownload.Add((type, entry));
                continue;
            }
            var info = new FileInfo(path);
            var changed = info.Length != entry.Size || entry.Modified != info.LastWriteTimeUtc;
            if (!changed)
                continue;
            if (!ctx.Settings.Game.CheckModsOnLaunch && info.Length == entry.Size)
                continue;
            string sha1;
            using (var stream = info.OpenRead())
                sha1 = Text.Sha1Hex(stream);
            if (entry.Hashes.Sha1 == null || sha1.Equals(entry.Hashes.Sha1, StringComparison.OrdinalIgnoreCase))
                continue;
            if (HasSource(entry))
                redownload.Add((type, entry));
            else if (type == ContentType.Mod && entry.Enabled)
                results.Add(new CheckResult(Id, Severity.Warning, TextRef.Of("pipeline.content.localChanged", ("name", entry.Name)),
                    [DisableAction(content, type, entry.File)], Key: $"hash:{entry.File}@{sha1[..8]}"));
        }

        if (redownload.Count > 0)
            results.Add(new CheckResult(Id, Severity.AutoFix, TextRef.Of("pipeline.content.redownload", ("count", redownload.Count)),
                [new CheckAction("redownload", new TextRef("pipeline.action.redownload"), (c, job) => RedownloadAsync(c, redownload, job), true)]));
        return Task.FromResult<IReadOnlyList<CheckResult>>(results.Count == 0 ? [CheckResult.Ok(Id)] : results);
    }

    private static bool HasSource(ContentEntry e) =>
        e.Source is ContentSource.Modrinth or ContentSource.CurseForge && e.VersionId != null;

    private async Task RedownloadAsync(LaunchContext ctx, List<(ContentType Type, ContentEntry Entry)> items, JobContext job)
    {
        var requests = new List<DownloadRequest>();
        foreach (var (type, entry) in items)
        {
            var version = await providers.Get(entry.Source).GetVersionAsync(entry.VersionId!, job.Token);
            var file = version?.Files.FirstOrDefault(f => f.FileName == entry.File) ?? version?.PrimaryFile;
            if (file == null || string.IsNullOrEmpty(file.Url))
                throw new AxoException("pipeline.content.redownloadFailed", new() { ["name"] = entry.Name });
            requests.Add(new DownloadRequest(file.Url, PathGuard.Combine(Path.Combine(ctx.GameDir, ContentFolders.Of(type)), entry.DiskName),
                file.Sha1, file.Sha512));
        }
        job.Status("pipeline.content.downloading");
        await downloads.DownloadAllAsync(requests, job.Range(0, 1), job.Token);
    }

    internal static CheckAction DisableAction(ContentManager content, ContentType type, string file) =>
        new("disable", new TextRef("pipeline.action.disableMod"), (c, job) => content.SetEnabledAsync(c.Instance.Id, type, file, false, job.Token));
}

/// <summary>Jars in <c>mods/</c>, die nicht in content.json stehen → Auto-Fix: als <c>local</c> eintragen.</summary>
public sealed class UnknownFilesCheck(ContentManager content) : ILaunchCheck
{
    public string Id => "unknownFiles";
    public LaunchPhase Phase => LaunchPhase.Content;

    public Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        var unknown = 0;
        foreach (var type in Enum.GetValues<ContentType>())
        {
            var dir = Path.Combine(ctx.GameDir, ContentFolders.Of(type));
            if (!Directory.Exists(dir))
                continue;
            var known = ctx.Content.ListOf(type).Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
            unknown += Directory.EnumerateFiles(dir).Select(Path.GetFileName)
                .Count(f => ContentManager.IsContentFile(type, f!) && !known.Contains(ContentManager.BaseName(f!))
                                                                   && !AxoModInstaller.IsAxoModFile(f!));
        }
        IReadOnlyList<CheckResult> results = unknown == 0
            ? [CheckResult.Ok(Id)]
            : [new CheckResult(Id, Severity.AutoFix, TextRef.Of("pipeline.content.unknown", ("count", unknown)),
                [new CheckAction("register", new TextRef("pipeline.action.register"), async (c, job) =>
                    c.Content = await content.SyncAsync(c.Instance.Id, job.Token), true)])];
        return Task.FromResult(results);
    }
}

/// <summary>Dieselbe Mod in zwei Versionen → Blocker "Neuere behalten".</summary>
public sealed class DuplicateModCheck(ContentManager content) : ILaunchCheck
{
    public string Id => "duplicate";
    public LaunchPhase Phase => LaunchPhase.Content;

    public Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        var results = ctx.Content.Mods
            .Where(m => m.Enabled && m.Meta?.ModId is { Length: > 0 } && m.Source != ContentSource.AxoClient)
            .GroupBy(m => m.Meta!.ModId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(m => m.Version ?? "", Comparer<string>.Create(VersionRange.Compare))
                    .ThenByDescending(m => m.AddedAt).ToList();
                var keep = ordered[0];
                var older = ordered.Skip(1).Select(m => m.File).ToList();
                return new CheckResult(Id, Severity.Blocker, TextRef.Of("pipeline.content.duplicate", ("name", keep.Name),
                        ("count", ordered.Count)),
                    [new CheckAction("keepNewest", TextRef.Of("pipeline.action.keepNewest"), async (c, job) =>
                    {
                        foreach (var file in older)
                            await content.SetEnabledAsync(c.Instance.Id, ContentType.Mod, file, false, job.Token);
                    }, true)], CanSilence: false);
            }).ToList();
        return Task.FromResult<IReadOnlyList<CheckResult>>(results.Count == 0 ? [CheckResult.Ok(Id)] : results);
    }
}

/// <summary>Legt die passende AxoClient-Mod in die Instanz oder entfernt sie (Auto-Fix bzw. Hinweis ohne Dialog).</summary>
public sealed class AxoModCheck(AxoModInstaller axoMod, ContentManager content) : ILaunchCheck
{
    public string Id => "axomod";
    public LaunchPhase Phase => LaunchPhase.Content;

    public Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        var instance = ctx.Instance;
        var supported = axoMod.IsSupported(instance.Minecraft.Version, instance.Loader.Type);
        if (!supported && instance.AxoMod)
            ctx.Hints.Add(new TextRef("pipeline.axomod.unsupported"));
        var wanted = supported && instance.AxoMod ? axoMod.Find(instance.Minecraft.Version, instance.Loader.Type) : null;
        var current = ctx.Content.Mods.FirstOrDefault(m => m.Source == ContentSource.AxoClient);
        var currentFile = current == null ? null : Path.Combine(ctx.GameDir, "mods", current.File);
        var upToDate = wanted == null
            ? current == null
            : current?.File == wanted.File && current.Version == AppInfo.Version && File.Exists(currentFile);
        var results = new List<CheckResult>();
        if (!upToDate)
            results.Add(new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.axomod.sync"),
                [new CheckAction("sync", new TextRef("pipeline.action.install"), (c, job) => SyncAsync(c, job), true)]));
        // Die Badge-Mod des alten Launchers (aus importierten Instanzen) würde neben der AxoMod doppelt laufen
        var legacy = wanted == null ? [] : ctx.Content.Mods.Where(m => m.Enabled && IsLegacyBadgeMod(m)).Select(m => m.File).ToList();
        if (legacy.Count > 0)
            results.Add(new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.axomod.legacy"),
                [new CheckAction("disableLegacy", new TextRef("pipeline.action.disableMod"), (c, job) => DisableAsync(c, legacy, job), true)]));
        return Task.FromResult<IReadOnlyList<CheckResult>>(results.Count == 0 ? [CheckResult.Ok(Id)] : results);
    }

    private static bool IsLegacyBadgeMod(ContentEntry mod) =>
        mod.Source != ContentSource.AxoClient
        && (mod.File.StartsWith("mclauncher-badge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mod.Meta?.ModId, "mclauncher_badge", StringComparison.OrdinalIgnoreCase));

    /// <summary>Deaktiviert statt löscht (umkehrbar); am Mod-Schutz vorbei, weil die Datei nie zum Pack gehört.</summary>
    private Task DisableAsync(LaunchContext ctx, IReadOnlyList<string> files, JobContext job) =>
        content.ChangeAsync(ctx.Instance.Id, doc =>
        {
            var dir = Path.Combine(ctx.GameDir, "mods");
            foreach (var mod in doc.Mods.Where(m => m.Enabled && files.Contains(m.File, StringComparer.OrdinalIgnoreCase)))
            {
                var from = Path.Combine(dir, mod.DiskName);
                mod.Enabled = false;
                if (File.Exists(from))
                    File.Move(from, Path.Combine(dir, mod.DiskName), overwrite: true);
            }
        }, job.Token);

    private async Task SyncAsync(LaunchContext ctx, JobContext job)
    {
        var entry = await axoMod.SyncAsync(Path.Combine(ctx.GameDir, "mods"), ctx.Instance.Minecraft.Version,
            ctx.Instance.Loader.Type, ctx.Instance.AxoMod, job.Token);
        await content.ChangeAsync(ctx.Instance.Id, doc =>
        {
            doc.Mods.RemoveAll(m => m.Source == ContentSource.AxoClient);
            if (entry != null)
                doc.Mods.Add(entry);
        }, job.Token);
    }
}

/// <summary>
/// „Freunde in Einzelspieler-Welten nachjoinen lassen“: legt e4mc (Modrinth) in Fabric- und Quilt-Instanzen.
/// Ist e4mc schon eingetragen – auch deaktiviert –, bleibt die Entscheidung beim Nutzer.
/// </summary>
public sealed class JoinRelayCheck(ContentManager content) : ILaunchCheck
{
    internal const string E4mcProject = "qANg5Jrr";

    public string Id => "joinRelay";
    public LaunchPhase Phase => LaunchPhase.Content;

    public Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct)
    {
        var wanted = ctx.Settings.Integrations.FriendJoinRelay
                     && ctx.Instance.Loader.Type is LoaderType.Fabric or LoaderType.Quilt
                     && !ctx.Content.Mods.Any(m => m.ProjectId == E4mcProject);
        IReadOnlyList<CheckResult> results = wanted
            ? [new CheckResult(Id, Severity.AutoFix, new TextRef("pipeline.relay.install"),
                [new CheckAction("install", new TextRef("pipeline.action.install"), InstallAsync, true)])]
            : [CheckResult.Ok(Id)];
        return Task.FromResult(results);
    }

    private async Task InstallAsync(LaunchContext ctx, JobContext job)
    {
        try
        {
            // e4mc gehört zur Launcher-Funktion "Nachjoinen" und ist vom Mod-Schutz ausgenommen
            await content.InstallAsync(ctx.Instance.Id, ContentType.Mod, new InstallRequest("modrinth", E4mcProject), job, ignoreModLock: true);
            ctx.Content = await content.SyncAsync(ctx.Instance.Id, job.Token);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Ohne e4mc startet das Spiel trotzdem – nur das Nachjoinen in Einzelspieler-Welten fehlt.
            AppLog.Warn($"e4mc konnte nicht installiert werden: {e.Message}");
            ctx.Hints.Add(new TextRef("pipeline.relay.unavailable"));
        }
    }
}
