using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Settings.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich Content.</summary>
    public class ContentHandler : BridgeHandler
    {
        private readonly IContentManager _content;
        private readonly ISettingsService _settings;
        private readonly IContentProviders _providers;
        private readonly IInstanceManager _instances;
        private readonly IJobManager _jobs;
        private readonly IGameLauncher _games;
        private readonly IUpdateScanner _updates;
        private readonly ICrashActions _crashActions;
        private readonly IModpackManager _modpacks;
        private readonly FileRefs _files;

        public ContentHandler(
            IContentManager content,
            ISettingsService settings,
            IContentProviders providers,
            IInstanceManager instances,
            IJobManager jobs,
            IGameLauncher games,
            IUpdateScanner updates,
            ICrashActions crashActions,
            IModpackManager modpacks,
            FileRefs files,
            IAppLog appLog,
            ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _content = content;
            _settings = settings;
            _providers = providers;
            _instances = instances;
            _jobs = jobs;
            _games = games;
            _updates = updates;
            _crashActions = crashActions;
            _modpacks = modpacks;
            _files = files;
        }

        public override void Register(MessageRouter router, MainWindow window)
        {
            router.On<ContentListData>("content.list", async (request, cancellationToken) => await _content.List(request.Id, request.Type, cancellationToken));

            router.On<SearchData>("content.search", async (request, cancellationToken) =>
            {
                await _settings.UpdateState(state =>
                {
                    if (request.Kind == ProjectKind.Modpack)
                    {
                        state.ContentSource.Modpacks = request.Source;
                    }
                    else
                    {
                        state.ContentSource.AddMods = request.Source;
                    }
                });
                return await _providers.Get(request.Source).Search(new SearchQuery(request.Kind, request.Text, request.Minecraft, request.Loader, request.Category, request.Sort,
                    request.Page, request.PageSize), cancellationToken);
            });

            router.On<ProjectData>("content.project", async (request, cancellationToken) => await _providers.Get(request.Source).GetProject(request.Id, cancellationToken));

            router.On<ProjectVersionsData>("content.projectVersions", async (request, cancellationToken) =>
            {
                string? minecraftVersion = null;
                LoaderType? loader = null;
                if (request.InstanceId != null && !request.AllVersions)
                {
                    var instance = await _instances.Get(request.InstanceId, cancellationToken);
                    minecraftVersion = instance.Minecraft.Version;
                    loader = instance.Loader.Type;
                }
                return await _providers.Get(request.Source).GetVersions(request.ProjectId, minecraftVersion, loader, request.Kind, cancellationToken);
            });

            router.On<InstallData>("content.install", (request, _) => Task.FromResult<object?>(StartJob(_jobs.Run("content.install",
                new TextRef("content.installing"),
                job => _content.Install(request.InstanceId, request.Type, new InstallRequest(request.Source, request.ProjectId, request.VersionId), job), request.InstanceId))));

            router.On<EnableData>("content.setEnabled", async (request, cancellationToken) =>
            {
                await _content.SetEnabled(request.Id, request.Type, request.File, request.Enabled, cancellationToken);
                return await _content.List(request.Id, request.Type, cancellationToken);
            });

            // Letztes Update zurücknehmen: die vorige Datei liegt in previous/
            router.On<RollbackData>("content.rollback", async (request, cancellationToken) =>
            {
                if (_games.IsRunning(request.Id))
                {
                    throw new AxoException("instances.running");
                }

                await _content.Rollback(request.Id, request.Type, request.File, cancellationToken);
                return await _content.List(request.Id, request.Type, cancellationToken);
            });

            router.On<RemoveData>("content.remove", async (request, cancellationToken) =>
            {
                await _content.Remove(request.Id, request.Type, request.Files.Take(500).ToList(), cancellationToken);
                return await _content.List(request.Id, request.Type, cancellationToken);
            });

            router.On<AddFilesData>("content.addFiles", async (request, cancellationToken) =>
            {
                foreach (var fileReference in request.FileRefs.Take(200))
                {
                    await _content.AddLocalFile(request.Id, request.Type, _files.Resolve(fileReference), cancellationToken);
                }

                _ = _content.IdentifyLocal(request.Id);
                return await _content.List(request.Id, request.Type, cancellationToken);
            });

            router.On<IdData>("content.identify", async (request, cancellationToken) =>
            {
                await _content.IdentifyLocal(request.Id, cancellationToken);
                return null;
            });

            router.On<VersionsForData>("content.versionsFor", async (request, cancellationToken) => await _content.VersionsFor(request.Id, request.Type, request.File, request.All, cancellationToken));

            router.On<IdData>("content.updates", async (request, cancellationToken) => await _updates.Scan(request.Id, cancellationToken));

            router.On<ApplyUpdatesData>("content.applyUpdates", (request, _) => Task.FromResult<object?>(StartJob(_jobs.Run("content.update",
                new TextRef("content.updatingAll"), job => _updates.Apply(request.Id, request.Updates.Take(500).ToList(), job), request.Id))));

            router.On<IdData>("content.repair", (request, _) => Task.FromResult<object?>(StartJob(_jobs.Run("content.repair",
                new TextRef("content.repairing"), job => _crashActions.Run(request.Id, "repairInstance", null, job), request.Id))));

            // ---- Modpacks ----
            router.On<ProjectData>("modpacks.versions", async (request, cancellationToken) => await _modpacks.Versions(request.Source, request.Id, cancellationToken));

            router.On<ModpackInstallData>("modpacks.install", (request, _) => Task.FromResult<object?>(StartJob(_jobs.Run("modpack.install",
                new TextRef("modpacks.installing"), job => _modpacks.InstallFromSource(request.Source, request.ProjectId, request.VersionId, job)))));

            router.On<ModpackUpdateData>("modpacks.update", (request, _) => Task.FromResult<object?>(StartJob(_jobs.Run("modpack.update",
                new TextRef("modpacks.updating"), job => _modpacks.Update(request.Id, request.VersionId, job), request.Id))));
        }
    }
}
