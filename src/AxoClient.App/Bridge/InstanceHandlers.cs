using AxoClient.Core.Common;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record IdData(string Id);

    private sealed record VersionListData(bool Snapshots = false, bool Old = false);

    private sealed record LoaderVersionsData(LoaderType Loader, string Minecraft);

    private sealed record CreateData(string Name, string Minecraft, LoaderType Loader, string? LoaderVersion, int? MemoryMb, string? IconRef);

    private sealed record UpdateData(string Id, string? Name, string? Minecraft, LoaderType? Loader, string? LoaderVersion, int? MemoryMb,
        bool ResetMemory, string? JavaArgs, bool? BannerAuto, bool? AxoMod);

    private sealed record ImageData(string Id, bool Banner, string? FileRef, string? Base64);
    private sealed record ModLockData(string Id, AxoClient.Core.Instances.ModLockLevel Level, bool AdoptCurrent = false);

    private sealed record CopyData(string Id, string Name);

    private sealed record ExportData(string Id, string Format, string FileRef);

    private sealed record ImportData(string FileRef);

    private sealed record WorldData(string Id, string Folder);

    private sealed record WorldImportData(string Id, List<string> FileRefs);

    private sealed record BackupFileData(string Id, string File);

    private sealed record ServerAddData(string Id, string Name, string Address, int? Index);

    private sealed record ServerEditData(string Id, int Index, string Name, string Address);

    private sealed record ServerIndexData(string Id, int Index);

    private sealed record ServerMoveData(string Id, int From, int To);

    private sealed record AddressData(string Address);

    private sealed record RenameData(string Id, string File, string Name);

    private static void RegisterInstances(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;

        r.On<VersionListData>("versions.list", async (d, ct) => (await host.Versions.ListAsync(ct))
            .Where(v => v.Type == VersionType.Release || (d.Snapshots && v.Type == VersionType.Snapshot) || (d.Old && v.Type == VersionType.Old))
            .Select(v => new { id = v.Id, type = v.Type, axo = host.AxoMod.SupportedMinecraftVersions.Contains(v.Id) })
            .ToList());

        r.On<LoaderVersionsData>("loaders.versions", async (d, ct) => await host.Loaders.ListVersionsAsync(d.Loader, d.Minecraft, ct));

        r.On("instances.list", async ct => await host.Instances.ListAsync(ct));

        r.On<IdData>("instances.get", async (d, ct) =>
        {
            var instance = await host.Instances.GetAsync(d.Id, ct);
            var summary = await host.Instances.SummaryAsync(instance, ct);
            await host.Settings.UpdateStateAsync(s => s.LastInstance = d.Id);
            return new
            {
                instance,
                summary,
                worlds = WorldManager.Count(Path.Combine(host.Paths.GameDir(d.Id), "saves")),
                axoModSupported = host.AxoMod.IsSupported(instance.Minecraft.Version, instance.Loader.Type),
                effectiveMemoryMb = instance.MemoryMb ?? host.Settings.Current.Defaults.MemoryMb
            };
        });

        r.On<IdData>("instances.activity", async (d, ct) =>
            await host.Activity.LoadAsync(host.Paths.PlaytimeJson(Text.RequireId(d.Id))));

        // Wo gespielt wurde: Server und Einzelspieler-Welten, mit Namen aus Welten- und Serverliste
        r.On<IdData>("instances.places", async (d, ct) =>
        {
            var log = await host.Places.LoadAsync(d.Id, ct);
            var worlds = (await host.Worlds.ListAsync(d.Id, ct)).ToDictionary(w => w.Folder, StringComparer.OrdinalIgnoreCase);
            static string Norm(string address) =>
                address.Trim().ToLowerInvariant() is var a && a.EndsWith(":25565", StringComparison.Ordinal) ? a[..^6] : address.Trim().ToLowerInvariant();
            var saved = host.ServerLists.List(d.Id).GroupBy(s => Norm(s.Address)).ToDictionary(g => g.Key, g => g.First());
            return log.Places.Select(p =>
            {
                if (p.Kind == "world")
                    return worlds.TryGetValue(p.Id, out var w)
                        ? new PlaceView("world", p.Id, w.Name, w.IconUrl, p.Seconds, p.LastPlayed, true)
                        : new PlaceView("world", p.Id, p.Id, null, p.Seconds, p.LastPlayed, false);
                return saved.TryGetValue(Norm(p.Id), out var s)
                    ? new PlaceView("server", p.Id, s.Name, s.IconUrl, p.Seconds, p.LastPlayed, true)
                    : new PlaceView("server", p.Id, p.Id, null, p.Seconds, p.LastPlayed, true);
            }).OrderByDescending(p => p.Seconds).ToList();
        });

        r.On<IdData>("instances.size", async (d, ct) => await Task.Run(() =>
        {
            var root = host.Paths.Instance(Text.RequireId(d.Id));
            var game = host.Paths.GameDir(d.Id);
            var worlds = WorldManager.DirectorySize(Path.Combine(game, "saves"));
            var mods = WorldManager.DirectorySize(Path.Combine(game, "mods"));
            var all = WorldManager.DirectorySize(root);
            return (object)new { worlds, mods, rest = Math.Max(0, all - worlds - mods) };
        }, ct));

        // Probleme einer Instanz ohne Start (Mods-Tab "Reparieren") und "Alles reparieren"
        r.On<IdData>("instances.diagnose", async (d, ct) => (await host.Pipeline.DiagnoseAsync(d.Id, ct))
            .Select((x, i) => new Core.Launch.IssueView($"{x.CheckId}-{i}", x.Severity, x.Message, x.Detail,
                x.Actions.Select(a => new Core.Launch.ActionView(a.Id, a.Label, a.Primary)).ToList(), false))
            .ToList());
        r.On<IdData>("instances.repairAll", async (d, _) => await host.Jobs.RunAsync("content.repair", new TextRef("content.repairing"),
            job => host.Pipeline.RepairAllAsync(d.Id, job), d.Id));

        r.On<CreateData>("instances.create", async (d, ct) =>
        {
            var loaderVersion = d.Loader == LoaderType.Vanilla
                ? null
                : await host.Loaders.ResolveVersionAsync(d.Loader, d.Minecraft, d.LoaderVersion, ct);
            var instance = await host.Instances.CreateAsync(new CreateInstanceRequest(d.Name, d.Minecraft, d.Loader, loaderVersion, d.MemoryMb),
                ct: ct);
            var type = await host.Versions.TypeOfAsync(d.Minecraft, ct);
            if (type is { } t && t != VersionType.Release)
            {
                instance.Minecraft = instance.Minecraft with { Type = t };
                await host.Instances.SaveAsync(instance, ct);
            }
            if (d.IconRef != null)
                await host.Instances.SetImageAsync(instance.Id, false, await File.ReadAllBytesAsync(ctx.Files.Resolve(d.IconRef), ct), ct);
            return instance;
        });

        r.On<UpdateData>("instances.update", async (d, ct) =>
        {
            if (host.Games.IsRunning(d.Id) && (d.Minecraft != null || d.Loader != null))
                throw new AxoException("instances.running");
            string? loaderVersion = d.LoaderVersion;
            if (d.Loader is { } loader and not LoaderType.Vanilla && d.Minecraft is { } mc && loaderVersion == null)
                loaderVersion = await host.Loaders.ResolveVersionAsync(loader, mc, null, ct);
            return await host.Instances.UpdateAsync(d.Id, new InstanceUpdate(d.Name, d.Minecraft, d.Loader, loaderVersion, d.MemoryMb,
                d.ResetMemory, d.JavaArgs, d.BannerAuto, d.AxoMod), ct);
        });

        r.On<ModLockData>("instances.setModLock", async (d, ct) =>
        {
            if (host.Games.IsRunning(d.Id))
                throw new AxoException("instances.running");
            return await host.Content.SetModLockAsync(Text.RequireId(d.Id), d.Level, d.AdoptCurrent, ct);
        });

        r.On<ImageData>("instances.setImage", async (d, ct) =>
        {
            byte[]? bytes = d.FileRef != null ? await File.ReadAllBytesAsync(ctx.Files.Resolve(d.FileRef), ct)
                : d.Base64 != null ? Convert.FromBase64String(d.Base64) : null;
            await host.Instances.SetImageAsync(d.Id, d.Banner, bytes, ct);
            return null;
        });

        r.On<IdData>("instances.delete", async (d, ct) =>
        {
            await host.Instances.DeleteAsync(d.Id, ct);
            host.Notifications.RemoveForInstance(d.Id);
            return null;
        });

        r.On<CopyData>("instances.copy", (d, _) => Task.FromResult<object?>(StartJob(host.Jobs.RunAsync("instance.copy",
            TextRef.Of("instances.copying", ("name", d.Name)), job => host.Instances.CopyAsync(d.Id, d.Name, job), d.Id))));

        r.On<ExportData>("instances.export", (d, _) =>
        {
            var target = ctx.Files.Resolve(d.FileRef);
            Task task = d.Format switch
            {
                "mrpack" => host.Jobs.RunAsync("instance.export", new TextRef("instances.exporting"),
                    job => host.Modpacks.ExportMrpackAsync(d.Id, target, job), d.Id),
                "backup" => host.Jobs.RunAsync("instance.export", new TextRef("instances.backingUp"),
                    job => host.Backups.ExportAsync(d.Id, target, job), d.Id),
                _ => throw new AxoException("errors.invalidInput")
            };
            return Task.FromResult<object?>(StartJob(task));
        });

        r.On<ImportData>("instances.import", async (d, _) =>
        {
            var path = ctx.Files.Resolve(d.FileRef);
            var instance = await host.Jobs.RunAsync("instance.import", TextRef.Of("instances.importing", ("file", Path.GetFileName(path))),
                job => host.Backups.ImportAsync(path, job));
            return instance;
        });

        // ---- Welten ----
        r.On<IdData>("worlds.list", async (d, ct) => await host.Worlds.ListAsync(d.Id, ct));
        r.On<WorldData>("worlds.backup", (d, _) => Task.FromResult<object?>(StartJob(host.Jobs.RunAsync("world.backup",
            new TextRef("worlds.backingUp"), job => host.Worlds.BackupAsync(d.Id, d.Folder, job), d.Id))));
        r.On<IdData>("worlds.backups", (d, _) => Task.FromResult<object?>(host.Worlds.ListBackups(d.Id)));
        r.On<BackupFileData>("worlds.restore", async (d, _) =>
            await host.Jobs.RunAsync("world.restore", new TextRef("worlds.restoring"), job => host.Worlds.RestoreBackupAsync(d.Id, d.File, job), d.Id));
        r.On<WorldImportData>("worlds.import", async (d, _) =>
        {
            var paths = d.FileRefs.Take(20).Select(ctx.Files.Resolve).ToList();
            return await host.Jobs.RunAsync("world.import", new TextRef("worlds.importing"), async job =>
            {
                var names = new List<string>();
                for (var i = 0; i < paths.Count; i++)
                {
                    try
                    {
                        names.Add(await host.Worlds.ImportAsync(d.Id, paths[i], job.Sub((double)i / paths.Count, (double)(i + 1) / paths.Count)));
                    }
                    catch (InvalidDataException)
                    {
                        throw new AxoException("worlds.importNoWorld");
                    }
                }
                return names;
            }, d.Id);
        });
        r.On<WorldData>("worlds.delete", async (d, ct) =>
        {
            if (host.Games.IsRunning(d.Id))
                throw new AxoException("instances.running");
            await host.Worlds.DeleteAsync(d.Id, d.Folder, ct);
            return null;
        });

        // ---- Serverliste der Instanz ----
        r.On<IdData>("serverlist.list", (d, _) => Task.FromResult<object?>(host.ServerLists.List(d.Id)));
        r.On<ServerAddData>("serverlist.add", async (d, _) =>
        {
            await host.ServerLists.AddAsync(d.Id, d.Name, d.Address, d.Index);
            return host.ServerLists.List(d.Id);
        });
        r.On<ServerEditData>("serverlist.edit", async (d, _) =>
        {
            await host.ServerLists.EditAsync(d.Id, d.Index, d.Name, d.Address);
            return host.ServerLists.List(d.Id);
        });
        r.On<ServerIndexData>("serverlist.remove", async (d, _) =>
        {
            await host.ServerLists.RemoveAsync(d.Id, d.Index);
            return host.ServerLists.List(d.Id);
        });
        r.On<ServerMoveData>("serverlist.move", async (d, _) =>
        {
            await host.ServerLists.MoveAsync(d.Id, d.From, d.To);
            return host.ServerLists.List(d.Id);
        });
        r.On<AddressData>("server.ping", async (d, ct) =>
            ServerPing.IsValidAddress(d.Address) ? await ServerPing.PingAsync(d.Address, ct: ct) : throw new AxoException("servers.addressInvalid"));

        // ---- Screenshots ----
        r.On<IdData>("screenshots.list", (d, _) => Task.FromResult<object?>(host.Screenshots.List(d.Id)));
        r.On<RenameData>("screenshots.rename", (d, _) =>
        {
            host.Screenshots.Rename(d.Id, d.File, d.Name);
            return Task.FromResult<object?>(host.Screenshots.List(d.Id));
        });
        r.On<BackupFileData>("screenshots.delete", (d, _) =>
        {
            host.Screenshots.Delete(d.Id, d.File);
            return Task.FromResult<object?>(host.Screenshots.List(d.Id));
        });
        r.On<BackupFileData>("screenshots.reveal", (d, _) =>
        {
            OpenInExplorer(host.Screenshots.PathOf(d.Id, d.File) ?? throw new AxoException("errors.notFound"));
            return Task.FromResult<object?>(null);
        });
    }
}
