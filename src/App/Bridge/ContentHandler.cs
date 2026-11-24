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

        public override void Register(MessageRouter r, MainWindow window)
        {
            r.On<ContentListData>("content.list", async (d, ct) => await _content.List(d.Id, d.Type, ct));

            r.On<SearchData>("content.search", async (d, ct) =>
            {
                await _settings.UpdateState(s =>
                {
                    if (d.Kind == ProjectKind.Modpack)
                    {
                        s.ContentSource.Modpacks = d.Source;
                    }
                    else
                    {
                        s.ContentSource.AddMods = d.Source;
                    }
                });
                return await _providers.Get(d.Source).Search(new SearchQuery(d.Kind, d.Text, d.Minecraft, d.Loader, d.Category, d.Sort,
                    d.Page, d.PageSize), ct);
            });

            r.On<ProjectData>("content.project", async (d, ct) => await _providers.Get(d.Source).GetProject(d.Id, ct));

            r.On<ProjectVersionsData>("content.projectVersions", async (d, ct) =>
            {
                string? mc = null;
                LoaderType? loader = null;
                if (d.InstanceId != null && !d.AllVersions)
                {
                    var instance = await _instances.Get(d.InstanceId, ct);
                    mc = instance.Minecraft.Version;
                    loader = instance.Loader.Type;
                }
                return await _providers.Get(d.Source).GetVersions(d.ProjectId, mc, loader, d.Kind, ct);
            });

            r.On<InstallData>("content.install", (d, _) => Task.FromResult<object?>(StartJob(_jobs.Run("content.install",
                new TextRef("content.installing"),
                job => _content.Install(d.InstanceId, d.Type, new InstallRequest(d.Source, d.ProjectId, d.VersionId), job), d.InstanceId))));

            r.On<EnableData>("content.setEnabled", async (d, ct) =>
            {
                await _content.SetEnabled(d.Id, d.Type, d.File, d.Enabled, ct);
                return await _content.List(d.Id, d.Type, ct);
            });

            // Letztes Update zurücknehmen: die vorige Datei liegt in previous/
            r.On<RollbackData>("content.rollback", async (d, ct) =>
            {
                if (_games.IsRunning(d.Id))
                {
                    throw new AxoException("instances.running");
                }

                await _content.Rollback(d.Id, d.Type, d.File, ct);
                return await _content.List(d.Id, d.Type, ct);
            });

            r.On<RemoveData>("content.remove", async (d, ct) =>
            {
                await _content.Remove(d.Id, d.Type, d.Files.Take(500).ToList(), ct);
                return await _content.List(d.Id, d.Type, ct);
            });

            r.On<AddFilesData>("content.addFiles", async (d, ct) =>
            {
                foreach (var fileRef in d.FileRefs.Take(200))
                {
                    await _content.AddLocalFile(d.Id, d.Type, _files.Resolve(fileRef), ct);
                }

                _ = _content.IdentifyLocal(d.Id);
                return await _content.List(d.Id, d.Type, ct);
            });

            r.On<IdData>("content.identify", async (d, ct) =>
            {
                await _content.IdentifyLocal(d.Id, ct);
                return null;
            });

            r.On<VersionsForData>("content.versionsFor", async (d, ct) => await _content.VersionsFor(d.Id, d.Type, d.File, d.All, ct));

            r.On<IdData>("content.updates", async (d, ct) => await _updates.Scan(d.Id, ct));

            r.On<ApplyUpdatesData>("content.applyUpdates", (d, _) => Task.FromResult<object?>(StartJob(_jobs.Run("content.update",
                new TextRef("content.updatingAll"), job => _updates.Apply(d.Id, d.Updates.Take(500).ToList(), job), d.Id))));

            r.On<IdData>("content.repair", (d, _) => Task.FromResult<object?>(StartJob(_jobs.Run("content.repair",
                new TextRef("content.repairing"), job => _crashActions.Run(d.Id, "repairInstance", null, job), d.Id))));

            // ---- Modpacks ----
            r.On<ProjectData>("modpacks.versions", async (d, ct) => await _modpacks.Versions(d.Source, d.Id, ct));

            r.On<ModpackInstallData>("modpacks.install", (d, _) => Task.FromResult<object?>(StartJob(_jobs.Run("modpack.install",
                new TextRef("modpacks.installing"), job => _modpacks.InstallFromSource(d.Source, d.ProjectId, d.VersionId, job)))));

            r.On<ModpackUpdateData>("modpacks.update", (d, _) => Task.FromResult<object?>(StartJob(_jobs.Run("modpack.update",
                new TextRef("modpacks.updating"), job => _modpacks.Update(d.Id, d.VersionId, job), d.Id))));
        }
    }
}
