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

        public override void Register(MessageRouter router, MainWindow window)
        {
            router.On<VersionListData>("versions.list", async (request, cancellationToken) => (await _versions.List(cancellationToken))
                .Where(gameVersion => gameVersion.Type == VersionType.Release || (request.Snapshots && gameVersion.Type == VersionType.Snapshot) || (request.Old && gameVersion.Type == VersionType.Old))
                .Select(gameVersion => new { id = gameVersion.Id, type = gameVersion.Type, axo = _axoMod.SupportedMinecraftVersions.Contains(gameVersion.Id) })
                .ToList());

            router.On<LoaderVersionsData>("loaders.versions", async (request, cancellationToken) => await _loaders.ListVersions(request.Loader, request.Minecraft, cancellationToken));

            router.On("instances.list", async cancellationToken => await _instances.List(cancellationToken));

            router.On<IdData>("instances.get", async (request, cancellationToken) =>
            {
                var instance = await _instances.Get(request.Id, cancellationToken);
                var summary = await _instances.Summary(instance, cancellationToken);
                await _settings.UpdateState(state => state.LastInstance = request.Id);
                return new
                {
                    instance,
                    summary,
                    worlds = _worlds.Count(Path.Combine(_paths.GameDirectory(request.Id), "saves")),
                    axoModSupported = _axoMod.IsSupported(instance.Minecraft.Version, instance.Loader.Type),
                    effectiveMemoryMb = instance.MemoryMb ?? _settings.Current.Defaults.MemoryMb
                };
            });

            router.On<IdData>("instances.activity", async (request, cancellationToken) =>
                await _activity.Load(_paths.PlaytimeJson(TextHelper.RequireId(request.Id))));

            // Wo gespielt wurde: Server und Einzelspieler-Welten, mit Namen aus Welten- und Serverliste
            router.On<IdData>("instances.places", async (request, cancellationToken) =>
            {
                var log = await _places.Load(request.Id, cancellationToken);
                var worlds = (await _worlds.List(request.Id, cancellationToken)).ToDictionary(worldInfo => worldInfo.Folder, StringComparer.OrdinalIgnoreCase);
                string Norm(string address) =>
                    address.Trim().ToLowerInvariant() is var normalized && normalized.EndsWith(":25565", StringComparison.Ordinal) ? normalized[..^6] : address.Trim().ToLowerInvariant();
                var saved = _serverLists.List(request.Id).GroupBy(server => Norm(server.Address)).ToDictionary(group => group.Key, group => group.First());
                return log.Places.Select(place =>
                {
                    if (place.Kind == "world")
                    {
                        return worlds.TryGetValue(place.Id, out var value)
                            ? new PlaceView("world", place.Id, value.Name, value.IconUrl, place.Seconds, place.LastPlayed, true)
                            : new PlaceView("world", place.Id, place.Id, null, place.Seconds, place.LastPlayed, false);
                    }

                    return saved.TryGetValue(Norm(place.Id), out var savedValue)
                        ? new PlaceView("server", place.Id, savedValue.Name, savedValue.IconUrl, place.Seconds, place.LastPlayed, true)
                        : new PlaceView("server", place.Id, place.Id, null, place.Seconds, place.LastPlayed, true);
                }).OrderByDescending(placeView => placeView.Seconds).ToList();
            });

            router.On<IdData>("instances.size", async (request, cancellationToken) => await Task.Run(() =>
            {
                var root = _paths.Instance(TextHelper.RequireId(request.Id));
                var game = _paths.GameDirectory(request.Id);
                var worlds = _directoryHelper.Size(Path.Combine(game, "saves"));
                var mods = _directoryHelper.Size(Path.Combine(game, "mods"));
                var all = _directoryHelper.Size(root);
                return (object)new { worlds, mods, rest = Math.Max(0, all - worlds - mods) };
            }, cancellationToken));

            // Probleme einer Instanz ohne Start (Mods-Tab "Reparieren") und "Alles reparieren"
            router.On<IdData>("instances.diagnose", async (request, cancellationToken) => (await _pipeline.Diagnose(request.Id, cancellationToken))
                .Select((result, index) => new IssueView($"{result.CheckId}-{index}", result.Severity, result.Message, result.Detail,
                    result.Actions.Select(action => new ActionView(action.Id, action.Label, action.Primary)).ToList(), false))
                .ToList());
            router.On<RepairOneData>("instances.repairOne", async (request, _) =>
            {
                // Id aus der Diagnose: "<Prüfung>-<Stelle>"
                var cut = request.IssueId.LastIndexOf('-');
                if (cut <= 0 || !int.TryParse(request.IssueId[(cut + 1)..], out var index))
                {
                    throw new AxoException("errors.invalidInput");
                }

                await _jobs.Run("content.repair", new TextRef("content.repairing"),
                    job => _pipeline.RepairOne(request.Id, request.IssueId[..cut], index, request.ActionId, job), request.Id);
                return true;
            });
            router.On<IdData>("instances.repairAll", async (request, _) => await _jobs.Run("content.repair", new TextRef("content.repairing"),
                job => _pipeline.RepairAll(request.Id, job), request.Id));

            router.On<CreateData>("instances.create", async (request, cancellationToken) =>
            {
                var loaderVersion = request.Loader == LoaderType.Vanilla
                    ? null
                    : await _loaders.ResolveVersion(request.Loader, request.Minecraft, request.LoaderVersion, cancellationToken);
                var instance = await _instances.Create(new CreateInstanceRequest(request.Name, request.Minecraft, request.Loader, loaderVersion, request.MemoryMb, ArtSeed: request.ArtSeed),
                    cancellationToken: cancellationToken);
                var type = await _versions.TypeOf(request.Minecraft, cancellationToken);
                if (type is { } versionType && versionType != VersionType.Release)
                {
                    instance.Minecraft = instance.Minecraft with { Type = versionType };
                    await _instances.Save(instance, cancellationToken);
                }
                if (request.IconRef != null)
                {
                    await _instances.SetImage(instance.Id, false, await File.ReadAllBytesAsync(_files.Resolve(request.IconRef), cancellationToken), cancellationToken);
                }

                return instance;
            });

            router.On<UpdateData>("instances.update", async (request, cancellationToken) =>
            {
                if (_games.IsRunning(request.Id) && (request.Minecraft != null || request.Loader != null))
                {
                    throw new AxoException("instances.running");
                }

                string? loaderVersion = request.LoaderVersion;
                if (request.Loader is { } loader and not LoaderType.Vanilla && request.Minecraft is { } minecraftVersion && loaderVersion == null)
                {
                    loaderVersion = await _loaders.ResolveVersion(loader, minecraftVersion, null, cancellationToken);
                }

                return await _instances.Update(request.Id, new InstanceUpdate(request.Name, request.Minecraft, request.Loader, loaderVersion, request.MemoryMb,
                    request.ResetMemory, request.JavaArgs, request.BannerAuto, request.AxoMod), cancellationToken);
            });

            // Nach einem Wechsel von Version oder Loader: Mods passend umstellen bzw. deaktivieren
            router.On<IdData>("instances.adaptMods", async (request, _) =>
            {
                if (_games.IsRunning(request.Id))
                {
                    throw new AxoException("instances.running");
                }

                return await _jobs.Run("content.adapt", new TextRef("content.adapt.title"),
                    job => _content.AdaptMods(request.Id, job), request.Id);
            });

            router.On<ModLockData>("instances.setModLock", async (request, cancellationToken) =>
            {
                if (_games.IsRunning(request.Id))
                {
                    throw new AxoException("instances.running");
                }

                return await _content.SetModLock(TextHelper.RequireId(request.Id), request.Level, request.AdoptCurrent, cancellationToken);
            });

            router.On<ImageData>("instances.setImage", async (request, cancellationToken) =>
            {
                byte[]? bytes = request.FileRef != null ? await File.ReadAllBytesAsync(_files.Resolve(request.FileRef), cancellationToken)
                    : request.Base64 != null ? Convert.FromBase64String(request.Base64) : null;
                await _instances.SetImage(request.Id, request.Banner, bytes, cancellationToken);
                return null;
            });

            // Hintergrund: Panorama (Blickrichtung) oder Farbverlauf; ein eigenes Bild setzt instances.setImage (banner)
            router.On<BackdropData>("instances.setBackdrop", async (request, cancellationToken) =>
            {
                await _instances.SetBackdrop(request.Id, request.GradientOnly, request.PanoramaView, cancellationToken);
                return null;
            });

            router.On<IdData>("instances.delete", async (request, cancellationToken) =>
            {
                await _instances.Delete(request.Id, cancellationToken);
                _notifications.RemoveForInstance(request.Id);
                return null;
            });

            router.On<CopyData>("instances.copy", (request, _) => Task.FromResult<object?>(StartJob(_jobs.Run("instance.copy",
                new TextRef("instances.copying", ("name", request.Name)), job => _instances.Copy(request.Id, request.Name, job), request.Id))));

            router.On<ExportData>("instances.export", (request, _) =>
            {
                var target = _files.Resolve(request.FileRef);
                Task task = request.Format switch
                {
                    "mrpack" => _jobs.Run("instance.export", new TextRef("instances.exporting"),
                        job => _modpacks.ExportMrpack(request.Id, target, job), request.Id),
                    "backup" => _jobs.Run("instance.export", new TextRef("instances.backingUp"),
                        job => _backups.Export(request.Id, target, job), request.Id),
                    _ => throw new AxoException("errors.invalidInput")
                };
                return Task.FromResult<object?>(StartJob(task));
            });

            router.On<ImportData>("instances.import", async (request, _) =>
            {
                var path = _files.Resolve(request.FileRef);
                var instance = await _jobs.Run("instance.import", new TextRef("instances.importing", ("file", Path.GetFileName(path))),
                    job => _backups.Import(path, job));
                return instance;
            });

            // ---- Welten ----
            router.On<IdData>("worlds.list", async (request, cancellationToken) => await _worlds.List(request.Id, cancellationToken));
            router.On<WorldData>("worlds.backup", (request, _) => Task.FromResult<object?>(StartJob(_jobs.Run("world.backup",
                new TextRef("worlds.backingUp"), job => _worlds.Backup(request.Id, request.Folder, job), request.Id))));
            // Vor einem Wechsel von Version oder Loader: wartet, bis alle Welten gesichert sind
            router.On<IdData>("worlds.backupAll", async (request, _) =>
            {
                if (_games.IsRunning(request.Id))
                {
                    throw new AxoException("instances.running");
                }

                return await _jobs.Run("world.backupAll", new TextRef("worlds.backingUpAll"), job => _worlds.BackupAll(request.Id, job), request.Id);
            });
            // ---- Datapacks (je Welt) ----
            router.On<WorldData>("datapacks.list", async (request, cancellationToken) => await _datapacks.List(request.Id, request.Folder, cancellationToken));
            router.On<DatapackInstallData>("datapacks.install", (request, _) => Task.FromResult<object?>(StartJob(_jobs.Run("datapack.install",
                new TextRef("content.installing"),
                job => _datapacks.Install(request.InstanceId, request.World, new InstallRequest(request.Source, request.ProjectId, request.VersionId), job), request.InstanceId))));
            router.On<DatapackFileData>("datapacks.setEnabled", async (request, cancellationToken) =>
            {
                await _datapacks.SetEnabled(request.Id, request.World, request.File, request.Enabled);
                return await _datapacks.List(request.Id, request.World, cancellationToken);
            });
            router.On<DatapackFileData>("datapacks.remove", async (request, cancellationToken) =>
            {
                if (_games.IsRunning(request.Id))
                {
                    throw new AxoException("instances.running");
                }

                await _datapacks.Remove(request.Id, request.World, request.File, cancellationToken);
                return await _datapacks.List(request.Id, request.World, cancellationToken);
            });
            router.On<IdData>("worlds.backups", (request, _) => Task.FromResult<object?>(_worlds.ListBackups(request.Id)));
            router.On<BackupFileData>("worlds.restore", async (request, _) =>
                await _jobs.Run("world.restore", new TextRef("worlds.restoring"), job => _worlds.RestoreBackup(request.Id, request.File, job), request.Id));
            router.On<WorldImportData>("worlds.import", async (request, _) =>
            {
                var paths = request.FileRefs.Take(20).Select(_files.Resolve).ToList();
                return await _jobs.Run("world.import", new TextRef("worlds.importing"), async job =>
                {
                    var names = new List<string>();
                    for (var index = 0; index < paths.Count; index++)
                    {
                        try
                        {
                            names.Add(await _worlds.Import(request.Id, paths[index], job.Sub((double)index / paths.Count, (double)(index + 1) / paths.Count)));
                        }
                        catch (InvalidDataException)
                        {
                            throw new AxoException("worlds.importNoWorld");
                        }
                    }
                    return names;
                }, request.Id);
            });
            router.On<WorldData>("worlds.delete", async (request, cancellationToken) =>
            {
                if (_games.IsRunning(request.Id))
                {
                    throw new AxoException("instances.running");
                }

                await _worlds.Delete(request.Id, request.Folder, cancellationToken);
                return null;
            });

            // ---- Serverliste der Instanz ----
            router.On<IdData>("serverlist.list", (request, _) => Task.FromResult<object?>(_serverLists.List(request.Id)));
            router.On<ServerAddData>("serverlist.add", async (request, _) =>
            {
                await _serverLists.Add(request.Id, request.Name, request.Address, request.Index);
                return _serverLists.List(request.Id);
            });
            router.On<ServerEditData>("serverlist.edit", async (request, _) =>
            {
                await _serverLists.Edit(request.Id, request.Index, request.Name, request.Address);
                return _serverLists.List(request.Id);
            });
            router.On<ServerIndexData>("serverlist.remove", async (request, _) =>
            {
                await _serverLists.Remove(request.Id, request.Index);
                return _serverLists.List(request.Id);
            });
            router.On<ServerMoveData>("serverlist.move", async (request, _) =>
            {
                await _serverLists.Move(request.Id, request.From, request.To);
                return _serverLists.List(request.Id);
            });
            router.On<AddressData>("server.ping", async (request, cancellationToken) =>
                _serverPing.IsValidAddress(request.Address) ? await _serverPing.Ping(request.Address, cancellationToken: cancellationToken) : throw new AxoException("servers.addressInvalid"));

            // ---- Screenshots ----
            router.On<IdData>("screenshots.list", (request, _) => Task.FromResult<object?>(_screenshots.List(request.Id)));
            router.On<RenameData>("screenshots.rename", (request, _) =>
            {
                _screenshots.Rename(request.Id, request.File, request.Name);
                return Task.FromResult<object?>(_screenshots.List(request.Id));
            });
            router.On<BackupFileData>("screenshots.delete", (request, _) =>
            {
                _screenshots.Delete(request.Id, request.File);
                return Task.FromResult<object?>(_screenshots.List(request.Id));
            });
            router.On<BackupFileData>("screenshots.reveal", (request, _) =>
            {
                OpenInExplorer(_screenshots.PathOf(request.Id, request.File) ?? throw new AxoException("errors.notFound"));
                return Task.FromResult<object?>(null);
            });
        }
    }
}
