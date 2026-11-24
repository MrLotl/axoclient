using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Instances;
using AxoClient.DataModel.Launch;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Settings.Contract;
using AxoClient.Social.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich Instance.</summary>
    public class InstanceHandler : BridgeHandler
    {
        private readonly IVersionManager _versions;
        private readonly IAxoModInstaller _axoMod;
        private readonly ILoaderManager _loaders;
        private readonly IInstanceManager _instances;
        private readonly ISettingsService _settings;
        private readonly AppPaths _paths;
        private readonly IActivityTracker _activity;
        private readonly IPlaceTracker _places;
        private readonly IWorldManager _worlds;
        private readonly IServerListManager _serverLists;
        private readonly ILaunchPipeline _pipeline;
        private readonly IJobManager _jobs;
        private readonly IGameLauncher _games;
        private readonly IContentManager _content;
        private readonly INotificationManager _notifications;
        private readonly IModpackManager _modpacks;
        private readonly IInstanceBackup _backups;
        private readonly IDatapackManager _datapacks;
        private readonly IScreenshotManager _screenshots;
        private readonly FileRefs _files;
        private readonly IDirectoryHelper _directoryHelper;
        private readonly IServerPing _serverPing;

        public InstanceHandler(
            IVersionManager versions,
            IAxoModInstaller axoMod,
            ILoaderManager loaders,
            IInstanceManager instances,
            ISettingsService settings,
            AppPaths paths,
            IActivityTracker activity,
            IPlaceTracker places,
            IWorldManager worlds,
            IServerListManager serverLists,
            ILaunchPipeline pipeline,
            IJobManager jobs,
            IGameLauncher games,
            IContentManager content,
            INotificationManager notifications,
            IModpackManager modpacks,
            IInstanceBackup backups,
            IDatapackManager datapacks,
            IScreenshotManager screenshots,
            FileRefs files,
            IDirectoryHelper directoryHelper,
            IServerPing serverPing,
            IAppLog appLog,
            ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _versions = versions;
            _axoMod = axoMod;
            _loaders = loaders;
            _instances = instances;
            _settings = settings;
            _paths = paths;
            _activity = activity;
            _places = places;
            _worlds = worlds;
            _serverLists = serverLists;
            _pipeline = pipeline;
            _jobs = jobs;
            _games = games;
            _content = content;
            _notifications = notifications;
            _modpacks = modpacks;
            _backups = backups;
            _datapacks = datapacks;
            _screenshots = screenshots;
            _files = files;
            _directoryHelper = directoryHelper;
            _serverPing = serverPing;
        }

        private sealed record ModLockData(string Id, ModLockLevel Level, bool AdoptCurrent = false);

        public override void Register(MessageRouter r, MainWindow window)
        {
            r.On<VersionListData>("versions.list", async (d, ct) => (await _versions.List(ct))
                .Where(v => v.Type == VersionType.Release || (d.Snapshots && v.Type == VersionType.Snapshot) || (d.Old && v.Type == VersionType.Old))
                .Select(v => new { id = v.Id, type = v.Type, axo = _axoMod.SupportedMinecraftVersions.Contains(v.Id) })
                .ToList());

            r.On<LoaderVersionsData>("loaders.versions", async (d, ct) => await _loaders.ListVersions(d.Loader, d.Minecraft, ct));

            r.On("instances.list", async ct => await _instances.List(ct));

            r.On<IdData>("instances.get", async (d, ct) =>
            {
                var instance = await _instances.Get(d.Id, ct);
                var summary = await _instances.Summary(instance, ct);
                await _settings.UpdateState(s => s.LastInstance = d.Id);
                return new
                {
                    instance,
                    summary,
                    worlds = _worlds.Count(Path.Combine(_paths.GameDir(d.Id), "saves")),
                    axoModSupported = _axoMod.IsSupported(instance.Minecraft.Version, instance.Loader.Type),
                    effectiveMemoryMb = instance.MemoryMb ?? _settings.Current.Defaults.MemoryMb
                };
            });

            r.On<IdData>("instances.activity", async (d, ct) =>
                await _activity.Load(_paths.PlaytimeJson(TextHelper.RequireId(d.Id))));

            // Wo gespielt wurde: Server und Einzelspieler-Welten, mit Namen aus Welten- und Serverliste
            r.On<IdData>("instances.places", async (d, ct) =>
            {
                var log = await _places.Load(d.Id, ct);
                var worlds = (await _worlds.List(d.Id, ct)).ToDictionary(w => w.Folder, StringComparer.OrdinalIgnoreCase);
                string Norm(string address) =>
                    address.Trim().ToLowerInvariant() is var a && a.EndsWith(":25565", StringComparison.Ordinal) ? a[..^6] : address.Trim().ToLowerInvariant();
                var saved = _serverLists.List(d.Id).GroupBy(s => Norm(s.Address)).ToDictionary(g => g.Key, g => g.First());
                return log.Places.Select(p =>
                {
                    if (p.Kind == "world")
                    {
                        return worlds.TryGetValue(p.Id, out var w)
                            ? new PlaceView("world", p.Id, w.Name, w.IconUrl, p.Seconds, p.LastPlayed, true)
                            : new PlaceView("world", p.Id, p.Id, null, p.Seconds, p.LastPlayed, false);
                    }

                    return saved.TryGetValue(Norm(p.Id), out var s)
                        ? new PlaceView("server", p.Id, s.Name, s.IconUrl, p.Seconds, p.LastPlayed, true)
                        : new PlaceView("server", p.Id, p.Id, null, p.Seconds, p.LastPlayed, true);
                }).OrderByDescending(p => p.Seconds).ToList();
            });

            r.On<IdData>("instances.size", async (d, ct) => await Task.Run(() =>
            {
                var root = _paths.Instance(TextHelper.RequireId(d.Id));
                var game = _paths.GameDir(d.Id);
                var worlds = _directoryHelper.Size(Path.Combine(game, "saves"));
                var mods = _directoryHelper.Size(Path.Combine(game, "mods"));
                var all = _directoryHelper.Size(root);
                return (object)new { worlds, mods, rest = Math.Max(0, all - worlds - mods) };
            }, ct));

            // Probleme einer Instanz ohne Start (Mods-Tab "Reparieren") und "Alles reparieren"
            r.On<IdData>("instances.diagnose", async (d, ct) => (await _pipeline.Diagnose(d.Id, ct))
                .Select((x, i) => new IssueView($"{x.CheckId}-{i}", x.Severity, x.Message, x.Detail,
                    x.Actions.Select(a => new ActionView(a.Id, a.Label, a.Primary)).ToList(), false))
                .ToList());
            r.On<RepairOneData>("instances.repairOne", async (d, _) =>
            {
                // Id aus der Diagnose: "<Prüfung>-<Stelle>"
                var cut = d.IssueId.LastIndexOf('-');
                if (cut <= 0 || !int.TryParse(d.IssueId[(cut + 1)..], out var index))
                {
                    throw new AxoException("errors.invalidInput");
                }

                await _jobs.Run("content.repair", new TextRef("content.repairing"),
                    job => _pipeline.RepairOne(d.Id, d.IssueId[..cut], index, d.ActionId, job), d.Id);
                return true;
            });
            r.On<IdData>("instances.repairAll", async (d, _) => await _jobs.Run("content.repair", new TextRef("content.repairing"),
                job => _pipeline.RepairAll(d.Id, job), d.Id));

            r.On<CreateData>("instances.create", async (d, ct) =>
            {
                var loaderVersion = d.Loader == LoaderType.Vanilla
                    ? null
                    : await _loaders.ResolveVersion(d.Loader, d.Minecraft, d.LoaderVersion, ct);
                var instance = await _instances.Create(new CreateInstanceRequest(d.Name, d.Minecraft, d.Loader, loaderVersion, d.MemoryMb, ArtSeed: d.ArtSeed),
                    ct: ct);
                var type = await _versions.TypeOf(d.Minecraft, ct);
                if (type is { } t && t != VersionType.Release)
                {
                    instance.Minecraft = instance.Minecraft with { Type = t };
                    await _instances.Save(instance, ct);
                }
                if (d.IconRef != null)
                {
                    await _instances.SetImage(instance.Id, false, await File.ReadAllBytesAsync(_files.Resolve(d.IconRef), ct), ct);
                }

                return instance;
            });

            r.On<UpdateData>("instances.update", async (d, ct) =>
            {
                if (_games.IsRunning(d.Id) && (d.Minecraft != null || d.Loader != null))
                {
                    throw new AxoException("instances.running");
                }

                string? loaderVersion = d.LoaderVersion;
                if (d.Loader is { } loader and not LoaderType.Vanilla && d.Minecraft is { } mc && loaderVersion == null)
                {
                    loaderVersion = await _loaders.ResolveVersion(loader, mc, null, ct);
                }

                return await _instances.Update(d.Id, new InstanceUpdate(d.Name, d.Minecraft, d.Loader, loaderVersion, d.MemoryMb,
                    d.ResetMemory, d.JavaArgs, d.BannerAuto, d.AxoMod), ct);
            });

            // Nach einem Wechsel von Version oder Loader: Mods passend umstellen bzw. deaktivieren
            r.On<IdData>("instances.adaptMods", async (d, _) =>
            {
                if (_games.IsRunning(d.Id))
                {
                    throw new AxoException("instances.running");
                }

                return await _jobs.Run("content.adapt", new TextRef("content.adapt.title"),
                    job => _content.AdaptMods(d.Id, job), d.Id);
            });

            r.On<ModLockData>("instances.setModLock", async (d, ct) =>
            {
                if (_games.IsRunning(d.Id))
                {
                    throw new AxoException("instances.running");
                }

                return await _content.SetModLock(TextHelper.RequireId(d.Id), d.Level, d.AdoptCurrent, ct);
            });

            r.On<ImageData>("instances.setImage", async (d, ct) =>
            {
                byte[]? bytes = d.FileRef != null ? await File.ReadAllBytesAsync(_files.Resolve(d.FileRef), ct)
                    : d.Base64 != null ? Convert.FromBase64String(d.Base64) : null;
                await _instances.SetImage(d.Id, d.Banner, bytes, ct);
                return null;
            });

            // Hintergrund: Panorama (Blickrichtung) oder Farbverlauf; ein eigenes Bild setzt instances.setImage (banner)
            r.On<BackdropData>("instances.setBackdrop", async (d, ct) =>
            {
                await _instances.SetBackdrop(d.Id, d.GradientOnly, d.PanoramaView, ct);
                return null;
            });

            r.On<IdData>("instances.delete", async (d, ct) =>
            {
                await _instances.Delete(d.Id, ct);
                _notifications.RemoveForInstance(d.Id);
                return null;
            });

            r.On<CopyData>("instances.copy", (d, _) => Task.FromResult<object?>(StartJob(_jobs.Run("instance.copy",
                new TextRef("instances.copying", ("name", d.Name)), job => _instances.Copy(d.Id, d.Name, job), d.Id))));

            r.On<ExportData>("instances.export", (d, _) =>
            {
                var target = _files.Resolve(d.FileRef);
                Task task = d.Format switch
                {
                    "mrpack" => _jobs.Run("instance.export", new TextRef("instances.exporting"),
                        job => _modpacks.ExportMrpack(d.Id, target, job), d.Id),
                    "backup" => _jobs.Run("instance.export", new TextRef("instances.backingUp"),
                        job => _backups.Export(d.Id, target, job), d.Id),
                    _ => throw new AxoException("errors.invalidInput")
                };
                return Task.FromResult<object?>(StartJob(task));
            });

            r.On<ImportData>("instances.import", async (d, _) =>
            {
                var path = _files.Resolve(d.FileRef);
                var instance = await _jobs.Run("instance.import", new TextRef("instances.importing", ("file", Path.GetFileName(path))),
                    job => _backups.Import(path, job));
                return instance;
            });

            // ---- Welten ----
            r.On<IdData>("worlds.list", async (d, ct) => await _worlds.List(d.Id, ct));
            r.On<WorldData>("worlds.backup", (d, _) => Task.FromResult<object?>(StartJob(_jobs.Run("world.backup",
                new TextRef("worlds.backingUp"), job => _worlds.Backup(d.Id, d.Folder, job), d.Id))));
            // Vor einem Wechsel von Version oder Loader: wartet, bis alle Welten gesichert sind
            r.On<IdData>("worlds.backupAll", async (d, _) =>
            {
                if (_games.IsRunning(d.Id))
                {
                    throw new AxoException("instances.running");
                }

                return await _jobs.Run("world.backupAll", new TextRef("worlds.backingUpAll"), job => _worlds.BackupAll(d.Id, job), d.Id);
            });
            // ---- Datapacks (je Welt) ----
            r.On<WorldData>("datapacks.list", async (d, ct) => await _datapacks.List(d.Id, d.Folder, ct));
            r.On<DatapackInstallData>("datapacks.install", (d, _) => Task.FromResult<object?>(StartJob(_jobs.Run("datapack.install",
                new TextRef("content.installing"),
                job => _datapacks.Install(d.InstanceId, d.World, new InstallRequest(d.Source, d.ProjectId, d.VersionId), job), d.InstanceId))));
            r.On<DatapackFileData>("datapacks.setEnabled", async (d, ct) =>
            {
                await _datapacks.SetEnabled(d.Id, d.World, d.File, d.Enabled);
                return await _datapacks.List(d.Id, d.World, ct);
            });
            r.On<DatapackFileData>("datapacks.remove", async (d, ct) =>
            {
                if (_games.IsRunning(d.Id))
                {
                    throw new AxoException("instances.running");
                }

                await _datapacks.Remove(d.Id, d.World, d.File, ct);
                return await _datapacks.List(d.Id, d.World, ct);
            });
            r.On<IdData>("worlds.backups", (d, _) => Task.FromResult<object?>(_worlds.ListBackups(d.Id)));
            r.On<BackupFileData>("worlds.restore", async (d, _) =>
                await _jobs.Run("world.restore", new TextRef("worlds.restoring"), job => _worlds.RestoreBackup(d.Id, d.File, job), d.Id));
            r.On<WorldImportData>("worlds.import", async (d, _) =>
            {
                var paths = d.FileRefs.Take(20).Select(_files.Resolve).ToList();
                return await _jobs.Run("world.import", new TextRef("worlds.importing"), async job =>
                {
                    var names = new List<string>();
                    for (var i = 0; i < paths.Count; i++)
                    {
                        try
                        {
                            names.Add(await _worlds.Import(d.Id, paths[i], job.Sub((double)i / paths.Count, (double)(i + 1) / paths.Count)));
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
                if (_games.IsRunning(d.Id))
                {
                    throw new AxoException("instances.running");
                }

                await _worlds.Delete(d.Id, d.Folder, ct);
                return null;
            });

            // ---- Serverliste der Instanz ----
            r.On<IdData>("serverlist.list", (d, _) => Task.FromResult<object?>(_serverLists.List(d.Id)));
            r.On<ServerAddData>("serverlist.add", async (d, _) =>
            {
                await _serverLists.Add(d.Id, d.Name, d.Address, d.Index);
                return _serverLists.List(d.Id);
            });
            r.On<ServerEditData>("serverlist.edit", async (d, _) =>
            {
                await _serverLists.Edit(d.Id, d.Index, d.Name, d.Address);
                return _serverLists.List(d.Id);
            });
            r.On<ServerIndexData>("serverlist.remove", async (d, _) =>
            {
                await _serverLists.Remove(d.Id, d.Index);
                return _serverLists.List(d.Id);
            });
            r.On<ServerMoveData>("serverlist.move", async (d, _) =>
            {
                await _serverLists.Move(d.Id, d.From, d.To);
                return _serverLists.List(d.Id);
            });
            r.On<AddressData>("server.ping", async (d, ct) =>
                _serverPing.IsValidAddress(d.Address) ? await _serverPing.Ping(d.Address, ct: ct) : throw new AxoException("servers.addressInvalid"));

            // ---- Screenshots ----
            r.On<IdData>("screenshots.list", (d, _) => Task.FromResult<object?>(_screenshots.List(d.Id)));
            r.On<RenameData>("screenshots.rename", (d, _) =>
            {
                _screenshots.Rename(d.Id, d.File, d.Name);
                return Task.FromResult<object?>(_screenshots.List(d.Id));
            });
            r.On<BackupFileData>("screenshots.delete", (d, _) =>
            {
                _screenshots.Delete(d.Id, d.File);
                return Task.FromResult<object?>(_screenshots.List(d.Id));
            });
            r.On<BackupFileData>("screenshots.reveal", (d, _) =>
            {
                OpenInExplorer(_screenshots.PathOf(d.Id, d.File) ?? throw new AxoException("errors.notFound"));
                return Task.FromResult<object?>(null);
            });
        }
    }
}
