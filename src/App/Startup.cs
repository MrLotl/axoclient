using AxoClient.Accounts;
using AxoClient.Accounts.Contract;
using AxoClient.App.Bridge;
using AxoClient.App.Overlay;
using AxoClient.Common;
using AxoClient.Content;
using AxoClient.Content.Contract;
using AxoClient.Diagnostics;
using AxoClient.Diagnostics.Contract;
using AxoClient.Game;
using AxoClient.Game.Contract;
using AxoClient.Import;
using AxoClient.Import.Contract;
using AxoClient.Instances;
using AxoClient.Instances.Contract;
using AxoClient.Integrations;
using AxoClient.Integrations.Contract;
using AxoClient.Launch;
using AxoClient.Launch.Checks;
using AxoClient.Launch.Contract;
using AxoClient.Servers;
using AxoClient.Servers.Contract;
using AxoClient.Settings;
using AxoClient.Settings.Contract;
using AxoClient.Skins;
using AxoClient.Skins.Contract;
using AxoClient.Social;
using AxoClient.Social.Contract;
using AxoClient.Updates;
using AxoClient.Updates.Contract;
using Microsoft.Extensions.DependencyInjection;

namespace AxoClient.App
{
    /// <summary>Meldet alle Dienste des Launchers beim Service-Container an (einmal pro Prozess, alles Singletons).</summary>
    public class Startup
    {
        private readonly AppPaths _paths;

        public Startup(AppPaths paths)
        {
            _paths = paths;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(_paths);
            services.AddSingleton(_paths.Fs);

            addHelpers(services);
            addCommon(services);
            addAccounts(services);
            addGame(services);
            addInstances(services);
            addContent(services);
            addLaunch(services);
            addIntegrations(services);
            addSocial(services);
            addSkins(services);
            addServers(services);
            addDiagnostics(services);
            addUpdates(services);
            addApp(services);
            addHandlers(services);
        }

        private void addHelpers(IServiceCollection services)
        {
            services.AddSingleton<IAppInfo, AppInfo>();
            services.AddSingleton<IAppLog, AppLog>();
            services.AddSingleton<ILogRedactor, LogRedactor>();
            services.AddSingleton<ITextHelper, TextHelper>();
            services.AddSingleton<IJsonHelper, JsonHelper>();
            services.AddSingleton<IJsonStoreFactory, JsonStoreFactory>();
            services.AddSingleton<IPathGuard, PathGuard>();
            services.AddSingleton<ISystemInfo, SystemInfo>();
            services.AddSingleton<IAppearance, Appearance>();
            services.AddSingleton<IExceptionHelper, ExceptionHelper>();
            services.AddSingleton<IUrlHelper, UrlHelper>();
            services.AddSingleton<IDirectoryHelper, DirectoryHelper>();
            services.AddSingleton<IHttpHandlerFactory, HttpHandlerFactory>();
            services.AddSingleton<IUuidHelper, UuidHelper>();
            services.AddSingleton<IGameVersionHelper, GameVersionHelper>();
            services.AddSingleton<ILoaderNames, LoaderNames>();
            services.AddSingleton<IContentFolders, ContentFolders>();
            services.AddSingleton<IModMetadataReader, ModMetadataReader>();
            services.AddSingleton<IModCompatibility, ModCompatibility>();
            services.AddSingleton<ISqliteReader, SqliteReader>();
            services.AddSingleton<IImageFormats, ImageFormats>();
            services.AddSingleton<IInstanceRules, InstanceRules>();
            services.AddSingleton<INbtReader, NbtReader>();
            services.AddSingleton<IServerPing, ServerPing>();
            services.AddSingleton<IVersionRange, VersionRange>();
            services.AddSingleton<IJavaArgumentsHelper, JavaArgumentsHelper>();
            services.AddSingleton<IServerImport, ServerImport>();
            services.AddSingleton<ISkinFiles, SkinFiles>();
            services.AddSingleton<IOptionsFile, OptionsFile>();
            services.AddSingleton<IInstallLocation, InstallLocation>();
        }

        private void addCommon(IServiceCollection services)
        {
            services.AddSingleton<IEventHub, EventHub>();
            services.AddSingleton<IJobManager, JobManager>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IApiClient>(provider => ActivatorUtilities.CreateInstance<ApiClient>(provider,
                provider.GetRequiredService<IHttpHandlerFactory>().Create(), _paths.Fs, _paths.ApiCache));
            services.AddSingleton(provider => provider.GetRequiredService<IApiClient>().Http);
            services.AddSingleton<IDownloadManager, DownloadManager>();
            services.AddSingleton<IProcessRunner, ProcessRunner>();
            services.AddSingleton<IActivityTracker, ActivityTracker>();
        }

        private void addAccounts(IServiceCollection services)
        {
            services.AddSingleton<ITokenStore>(provider => ActivatorUtilities.CreateInstance<TokenStore>(provider, _paths.Fs, _paths.TokensFile));
            services.AddSingleton<IDevAuthProvider, DevAuthProvider>();
            services.AddSingleton<IAuthProvider, MicrosoftAuthProvider>();
            if (Environment.GetEnvironmentVariable("AXOCLIENT_DEV") == "1")
            {
                services.AddSingleton<IAuthProvider>(provider => provider.GetRequiredService<IDevAuthProvider>());
            }
            services.AddSingleton<IAccountManager, AccountManager>();
        }

        private void addGame(IServiceCollection services)
        {
            services.AddSingleton<IVersionManager, VersionManager>();
            services.AddSingleton<ILoaderInstaller, FabricLoader>();
            services.AddSingleton<ILoaderInstaller, QuiltLoader>();
            services.AddSingleton<ILoaderInstaller, ForgeLoader>();
            services.AddSingleton<ILoaderInstaller, NeoForgeLoader>();
            services.AddSingleton<ILoaderManager, LoaderManager>();
            services.AddSingleton<IJavaManager, JavaManager>();
            services.AddSingleton<IMinecraftInstaller, MinecraftInstaller>();
            services.AddSingleton<IAxoModInstaller, AxoModInstaller>();
            services.AddSingleton<IPanoramaService, PanoramaService>();
        }

        private void addInstances(IServiceCollection services)
        {
            services.AddSingleton<IInstanceManager, InstanceManager>();
            services.AddSingleton<IWorldManager, WorldManager>();
            services.AddSingleton<IScreenshotManager, ScreenshotManager>();
            services.AddSingleton<IServerListManager, ServerListManager>();
            services.AddSingleton<IInstanceBackup, InstanceBackup>();
            services.AddSingleton<IPlaceTracker, PlaceTracker>();
        }

        private void addContent(IServiceCollection services)
        {
            services.AddSingleton<IModrinthProvider, ModrinthProvider>();
            services.AddSingleton<IContentProvider>(provider => provider.GetRequiredService<IModrinthProvider>());
            services.AddSingleton<IContentProvider>(provider => ActivatorUtilities.CreateInstance<CurseForgeProvider>(provider,
                provider.GetRequiredService<IAppInfo>().ServiceUrl, AppInfo.CurseForgeEnabled));
            services.AddSingleton<IContentProviders, ContentProviders>();
            services.AddSingleton<IContentManager, ContentManager>();
            services.AddSingleton<IDatapackManager, DatapackManager>();
            services.AddSingleton<IUpdateScanner, UpdateScanner>();
            services.AddSingleton<IModpackManager, ModpackManager>();
            services.AddSingleton<IForeignScanner, ForeignScanner>();
            services.AddSingleton<IForeignImporter, ForeignImporter>();
            services.AddSingleton<IInstanceTransfer, InstanceTransfer>();
        }

        private void addLaunch(IServiceCollection services)
        {
            services.AddSingleton<LaunchPromptBridge>();
            services.AddSingleton<ILaunchPrompt>(provider => provider.GetRequiredService<LaunchPromptBridge>());

            // Reihenfolge = Reihenfolge der Prüfungen vor dem Start
            services.AddSingleton<ILaunchCheck, AccountCheck>();
            services.AddSingleton<ILaunchCheck, InternetCheck>();
            services.AddSingleton<ILaunchCheck, DiskSpaceCheck>();
            services.AddSingleton<ILaunchCheck, AlreadyRunningCheck>();
            services.AddSingleton<ILaunchCheck, GameFilesCheck>();
            services.AddSingleton<ILaunchCheck, JavaCheck>();
            services.AddSingleton<ILaunchCheck, ContentFilesCheck>();
            services.AddSingleton<ILaunchCheck, UnknownFilesCheck>();
            services.AddSingleton<ILaunchCheck, DuplicateModCheck>();
            services.AddSingleton<ILaunchCheck, AxoModCheck>();
            services.AddSingleton<ILaunchCheck, JoinRelayCheck>();
            services.AddSingleton<ILaunchCheck, CompatibilityCheck>();
            services.AddSingleton<ILaunchCheck, MemoryCheck>();
            services.AddSingleton<ILaunchCheck, JavaArgsCheck>();
            services.AddSingleton<ILaunchPipeline, LaunchPipeline>();

            services.AddSingleton<ICrashAnalyzer, CrashAnalyzer>();
            services.AddSingleton<IStorageService, StorageService>();
            services.AddSingleton<ICrashActions, CrashActions>();

            // Der SocialService braucht den GameLauncher und umgekehrt; der Stellvertreter löst den Kreis
            services.AddSingleton<SocialHookProxy>();
            services.AddSingleton<IGameLaunchHook>(provider => provider.GetRequiredService<IGameActivity>());
            services.AddSingleton<IGameLaunchHook>(provider => provider.GetRequiredService<IAxoModBridge>());
            services.AddSingleton<IGameLaunchHook>(provider => provider.GetRequiredService<IPlaceTracker>());
            services.AddSingleton<IGameLaunchHook>(provider => provider.GetRequiredService<SocialHookProxy>());
            services.AddSingleton<IGameLauncher, GameLauncher>();
        }

        private void addIntegrations(IServiceCollection services)
        {
            services.AddSingleton<IGameActivity, GameActivity>();
            services.AddSingleton<IAxoModBridge, AxoModBridge>();
            services.AddSingleton<IDiscordIntegration, DiscordIntegration>();
            services.AddSingleton<ILiveChannel, LiveChannel>();
            services.AddSingleton<IEmoteLibrary, EmoteLibrary>();
            services.AddSingleton<IIngameLink, IngameLink>();
        }

        private void addSocial(IServiceCollection services)
        {
            services.AddSingleton<INotificationManager, NotificationManager>();
            services.AddSingleton<IOnlineClient, OnlineClient>();
            services.AddSingleton<ISocialService, SocialService>();
            services.AddSingleton<IInboxManager, InboxManager>();
        }

        private void addSkins(IServiceCollection services)
        {
            services.AddSingleton<IMojangApi, MojangApi>();
            services.AddSingleton<ISkinManager, SkinManager>();
            services.AddSingleton<ICapeManager, CapeManager>();
            services.AddSingleton<ICapeService, CapeService>();
        }

        private void addServers(IServiceCollection services)
        {
            services.AddSingleton<IServerSoftware, VanillaServer>();
            services.AddSingleton<IServerSoftware, PaperServer>();
            services.AddSingleton<IServerSoftware, FabricServer>();
            services.AddSingleton<IServerSoftware, VelocityServer>();
            services.AddSingleton<IServerJava, ServerJava>();
            services.AddSingleton<IPortForwarding, PortForwarding>();
            services.AddSingleton<ILocalServerManager, LocalServerManager>();
        }

        private void addDiagnostics(IServiceCollection services)
        {
            services.AddSingleton<IDiagnosticCheck, SystemDiagnostic>();
            services.AddSingleton<IDiagnosticCheck, PathDiagnostic>();
            services.AddSingleton<IDiagnosticCheck, JavaDiagnostic>();
            services.AddSingleton<IDiagnosticCheck, NetworkDiagnostic>();
            services.AddSingleton<IDiagnosticCheck, LauncherFilesDiagnostic>();
            services.AddSingleton<IDiagnosticCheck, AccountDiagnostic>();
            services.AddSingleton<IDiagnosticCheck, InstancesDiagnostic>();
            services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        }

        private void addUpdates(IServiceCollection services)
        {
            // Während ein Spiel oder Server läuft, wird kein Update eingespielt
            services.AddSingleton<IUpdateService>(provider => ActivatorUtilities.CreateInstance<UpdateService>(provider,
                new Func<bool>(() => provider.GetRequiredService<IProcessRunner>().AnyRunning())));
        }

        private void addApp(IServiceCollection services)
        {
            services.AddSingleton<NativeMessage>();
            services.AddSingleton<Installer>();
            services.AddSingleton<SingleInstance>();
            services.AddSingleton<AppHost>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<SchemeHandler>();
            services.AddSingleton<UiServer>();
            services.AddSingleton<FileRefs>();
            services.AddSingleton<WindowChrome>();
            services.AddSingleton<GameWindow>();
            services.AddSingleton<ToastTexts>();
            services.AddSingleton<McFontLoader>();
            services.AddSingleton<OverlayRenderer>();
            services.AddSingleton<PrivateOverlay>();
        }

        private void addHandlers(IServiceCollection services)
        {
            services.AddSingleton<BridgeHandler, AppHandler>();
            services.AddSingleton<BridgeHandler, AccountHandler>();
            services.AddSingleton<BridgeHandler, InstanceHandler>();
            services.AddSingleton<BridgeHandler, ContentHandler>();
            services.AddSingleton<BridgeHandler, LaunchHandler>();
            services.AddSingleton<BridgeHandler, ServerHandler>();
            services.AddSingleton<BridgeHandler, SkinHandler>();
            services.AddSingleton<BridgeHandler, EmoteHandler>();
            services.AddSingleton<BridgeHandler, SocialHandler>();
            services.AddSingleton<BridgeHandler, SettingsHandler>();
            services.AddSingleton<BridgeHandler, ImportHandler>();
        }
    }
}
