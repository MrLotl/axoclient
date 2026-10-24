using AxoClient.Core.Accounts;
using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Diagnostics;
using AxoClient.Core.Game;
using AxoClient.Core.Import;
using AxoClient.Core.Instances;
using AxoClient.Core.Integrations;
using AxoClient.Core.Launch;
using AxoClient.Core.Launch.Checks;
using AxoClient.Core.Servers;
using AxoClient.Core.Skins;
using AxoClient.Core.Social;
using AxoClient.Core.Updates;
using AxoClient.App.Bridge;

namespace AxoClient.App;

/// <summary>Kompositionswurzel: alle Dienste des Core, einmal pro Launcher-Prozess.</summary>
public sealed class AppHost : IAsyncDisposable
{
    public AppHost(AppPaths paths)
    {
        Paths = paths;
        paths.EnsureCreated();
        AppLog.Initialize(paths.Logs);
        AppLog.Prune();

        Events = new EventHub();
        Jobs = new JobManager(Events);
        Settings = new SettingsService(paths, Events);
        var handler = ApiClient.CreateDefaultHandler();
        Api = new ApiClient(handler, paths.Fs, paths.ApiCache);
        Downloads = new DownloadManager(Api, paths.Fs);
        Processes = new ProcessRunner();
        Activity = new ActivityTracker(paths.Fs);
        Tokens = new TokenStore(paths.Fs, paths.TokensFile);

        var providers = new List<IAuthProvider> { new MicrosoftAuthProvider(Api.Http, Tokens) };
        if (DevAuthProvider.Enabled)
            providers.Add(DevAuth);
        Accounts = new AccountManager(paths, providers, Events);

        Versions = new VersionManager(Api);
        Loaders = new LoaderManager([new FabricLoader(Api), new QuiltLoader(Api), new ForgeLoader(Api), new NeoForgeLoader(Api)]);
        Java = new JavaManager(paths);
        Installer = new GameInstaller(paths, Loaders, Api.Http);
        AxoMod = new AxoModInstaller();

        Instances = new InstanceManager(paths, Events, Processes);
        Worlds = new WorldManager(paths);
        Screenshots = new ScreenshotManager(paths);
        ServerLists = new ServerListManager(paths);

        Modrinth = new ModrinthProvider(Api);
        Providers = new ContentProviders([Modrinth, new CurseForgeProvider(Api, AppInfo.ServiceUrl, AppInfo.CurseForgeEnabled)]);
        Content = new ContentManager(Instances, Providers, Downloads, Events);
        Updates = new UpdateScanner(Instances, Content, Modrinth);
        Modpacks = new ModpackManager(paths, Instances, Content, Providers, Modrinth, Downloads, Api);
        Backups = new InstanceBackup(paths, Instances, Content, Modpacks);
        Foreign = new ForeignScanner();
        ForeignImport = new ForeignImporter(Foreign, Instances, Content);
        Transfer = new InstanceTransfer(Foreign, Instances, Content, ServerLists);

        Prompt = new LaunchPromptBridge(Events);
        Pipeline = new LaunchPipeline(
        [
            new AccountCheck(Accounts),
            new InternetCheck(Api, Installer),
            new DiskSpaceCheck(paths, Installer),
            new AlreadyRunningCheck(Processes),
            new GameFilesCheck(Installer, Loaders, Instances),
            new JavaCheck(Java, Settings),
            new ContentFilesCheck(Providers, Downloads, Content),
            new UnknownFilesCheck(Content),
            new DuplicateModCheck(Content),
            new AxoModCheck(AxoMod, Content),
            new JoinRelayCheck(Content),
            new CompatibilityCheck(Content, Providers),
            new MemoryCheck(Instances),
            new JavaArgsCheck(Instances)
        ], Prompt, Instances, Installer, Settings);
        CrashAnalyzer = new CrashAnalyzer();
        CrashActions = new CrashActions(Instances, Content, Installer, Updates, Settings);

        // Erweiterungen rund um den Spielstart
        PlayActivity = new GameActivity(Settings);
        Bridge = new AxoModBridge();
        Places = new PlaceTracker(paths);
        Notifications = new NotificationManager(Settings, Events);
        Online = new OnlineClient(Api, Accounts, Tokens, Events);
        var socialHook = new SocialHookProxy();
        Games = new GameLauncher(Pipeline, Installer, Processes, Jobs, Instances, Activity, CrashAnalyzer, paths, Events,
            [PlayActivity, Bridge, Places, socialHook]);
        Social = new SocialService(Online, Notifications, Settings, Games, Instances, ServerLists, Events);
        socialHook.Target = Social;
        Inbox = new InboxManager(Online, Social, Instances, Content, ServerLists, Jobs);
        PlayActivity.JoinAddressChanged += address => Social.SetJoinAddress(address);
        Discord = new DiscordIntegration(Settings, PlayActivity);

        Mojang = new MojangApi(Api, Accounts);
        Skins = new SkinManager(paths, Mojang, Accounts, Events);
        Capes = new CapeManager(paths, Accounts, Events);
        CapeService = new CapeService(Online, Capes, Accounts);
        // Laufende Spiele zeigen das neue Axo-Cape sofort, ohne auf die nächste Abfrage beim Dienst zu warten
        CapeService.ActiveChanged += (uuid, cape) => _ = Bridge.SendAsync(null, "cape", new { uuid, cape });
        // Ergebnis einer Cape-Prüfung (Benachrichtigung vom Dienst) in die eigene Cape-Liste übernehmen
        Social.RemoteNotification += n => _ = ApplyCapeReviewAsync(n);

        // Emotes: Mod → Launcher → Dienst (Raum des Servers) → andere Launcher → deren Mod
        Live = new LiveChannel(Online);
        PlayActivity.JoinAddressChanged += address => Live.SetServer(address);
        Emotes = new EmoteLibrary(paths, Events);
        // Axo-Menü im Spiel: Freunde, Nachjoinen, Capes, Emotes, Einblendungen
        Ingame = new IngameLink(Bridge, Social, Inbox, Online, CapeService, Capes, Mojang, Accounts, Skins, Instances, ServerLists, Content,
            Modrinth, Games, PlayActivity, Live, Emotes, Api, Events, Jobs);

        var softwares = new List<IServerSoftware>
        {
            new VanillaServer(Api, Versions), new PaperServer(Api), new FabricServer(Api), new VelocityServer(Api)
        };
        LocalServers = new LocalServerManager(paths, softwares, new ServerJava(paths, Api, Downloads, Java), Downloads, Processes, Jobs,
            Activity, CrashAnalyzer, new PortForwarding(), Events);

        LauncherUpdates = new UpdateService(Settings, Events, Api, Downloads, paths, () => Processes.AnyRunning());
        Storage = new StorageService(paths, Instances);
        Diagnostics = new DiagnosticsService(
        [
            new SystemDiagnostic(paths), new PathDiagnostic(paths), new JavaDiagnostic(Java), new NetworkDiagnostic(Api),
            new LauncherFilesDiagnostic(paths), new AccountDiagnostic(Accounts, Online), new InstancesDiagnostic(Instances, Pipeline)
        ]);

        // Lokale Benachrichtigungen aus Core-Ereignissen
        Events.Published += OnEvent;
    }

    public AppPaths Paths { get; }
    public EventHub Events { get; }
    public JobManager Jobs { get; }
    public SettingsService Settings { get; }
    public ApiClient Api { get; }
    public DownloadManager Downloads { get; }
    public ProcessRunner Processes { get; }
    public ActivityTracker Activity { get; }
    public TokenStore Tokens { get; }
    public DevAuthProvider DevAuth { get; } = new();
    public AccountManager Accounts { get; }
    public VersionManager Versions { get; }
    public LoaderManager Loaders { get; }
    public JavaManager Java { get; }
    public GameInstaller Installer { get; }
    public AxoModInstaller AxoMod { get; }
    public InstanceManager Instances { get; }
    public WorldManager Worlds { get; }
    public ScreenshotManager Screenshots { get; }
    public ServerListManager ServerLists { get; }
    public ModrinthProvider Modrinth { get; }
    public ContentProviders Providers { get; }
    public ContentManager Content { get; }
    public UpdateScanner Updates { get; }
    public ModpackManager Modpacks { get; }
    public InstanceBackup Backups { get; }
    public ForeignScanner Foreign { get; }
    public ForeignImporter ForeignImport { get; }
    public InstanceTransfer Transfer { get; }
    public LaunchPromptBridge Prompt { get; }
    public LaunchPipeline Pipeline { get; }
    public CrashAnalyzer CrashAnalyzer { get; }
    public CrashActions CrashActions { get; }
    public GameActivity PlayActivity { get; }
    public AxoModBridge Bridge { get; }
    public PlaceTracker Places { get; }
    public LiveChannel Live { get; }
    public EmoteLibrary Emotes { get; }
    public IngameLink Ingame { get; }
    public NotificationManager Notifications { get; }
    public OnlineClient Online { get; }
    public GameLauncher Games { get; }
    public SocialService Social { get; }
    public InboxManager Inbox { get; }
    public DiscordIntegration Discord { get; }
    public MojangApi Mojang { get; }
    public SkinManager Skins { get; }
    public CapeManager Capes { get; }
    public CapeService CapeService { get; }
    public LocalServerManager LocalServers { get; }
    public UpdateService LauncherUpdates { get; }
    public StorageService Storage { get; }
    public DiagnosticsService Diagnostics { get; }

    public async Task StartAsync()
    {
        await Settings.LoadAsync();
        await Accounts.LoadAsync();
        Discord.Start();
        Bridge.Start();
        Ingame.Start();
        Social.Start();
        LauncherUpdates.StartBackgroundChecks();
        _ = DailyModUpdateCheckAsync();
        _ = WatchDiskSpaceAsync();
    }

    /// <summary>Core-Ereignis "notify" → Glocke (mit den Regeln aus den Einstellungen).</summary>
    private void OnEvent(string type, object? data)
    {
        if (type != "notify" || data == null)
            return;
        try
        {
            var json = System.Text.Json.JsonSerializer.SerializeToElement(data, Json.Options);
            var kind = json.GetProperty("type").GetString() ?? "hint";
            var text = System.Text.Json.JsonSerializer.Deserialize<TextRef>(json.GetProperty("text").GetRawText(), Json.Options)!;
            if (kind == "hint")
            {
                Events.Publish("toast", new { text });
                return;
            }
            Notifications.Add(kind, text);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            AppLog.Warn("Benachrichtigung konnte nicht gelesen werden", ex);
        }
    }

    /// <summary>"capeReview" vom Dienst: Status des eigenen Capes (freigegeben/abgelehnt, Grund) übernehmen.</summary>
    private async Task ApplyCapeReviewAsync(ServiceNotification n)
    {
        if (n.Kind != "capeReview" || n.Data == null)
            return;
        try
        {
            var data = System.Text.Json.JsonSerializer.SerializeToElement(n.Data, Json.Options);
            var localId = data.TryGetProperty("localId", out var l) ? l.GetString() : null;
            var approved = data.TryGetProperty("approved", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.True;
            var reason = data.TryGetProperty("reason", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.String ? r.GetString() : null;
            if (localId != null)
                await CapeService.ApplyReviewAsync(localId, approved, reason, CancellationToken.None);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or IOException or AxoException)
        {
            AppLog.Warn("Cape-Prüfung konnte nicht übernommen werden", ex);
        }
    }

    /// <summary>Mod- und Modpack-Updates höchstens einmal am Tag, gesammelt pro Instanz.</summary>
    private async Task DailyModUpdateCheckAsync()
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        while (true)
        {
            try
            {
                foreach (var instance in await Instances.LoadAllAsync())
                {
                    var updates = await Updates.ScanAsync(instance.Id, CancellationToken.None);
                    if (updates.Count > 0)
                        Notifications.Add("modUpdates", TextRef.Of("notifications.modUpdates", ("count", updates.Count), ("instance", instance.Name)),
                            new() { ["instanceId"] = instance.Id }, $"modUpdates:{instance.Id}");
                    if (instance.Origin is { Type: OriginType.Modpack, Source: "modrinth", ProjectId: { } pid, VersionId: { } vid })
                    {
                        var latest = (await Modrinth.GetVersionsAsync(pid, null, null, ProjectKind.Modpack, CancellationToken.None)).FirstOrDefault();
                        if (latest != null && latest.Id != vid)
                            Notifications.Add("modpackUpdates", TextRef.Of("notifications.modpackUpdate", ("instance", instance.Name),
                                ("version", latest.VersionNumber)), new() { ["instanceId"] = instance.Id }, $"modpack:{instance.Id}");
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                AppLog.Warn("Tägliche Update-Prüfung fehlgeschlagen", ex);
            }
            await Task.Delay(TimeSpan.FromHours(24));
        }
    }

    private async Task WatchDiskSpaceAsync()
    {
        while (true)
        {
            if (SystemInfo.FreeDiskBytes(Paths.Root) < 5L * 1024 * 1024 * 1024)
                Notifications.Add("lowDiskSpace", TextRef.Of("notifications.lowDiskSpace",
                    ("freeGb", SystemInfo.FreeDiskBytes(Paths.Root) / 1024 / 1024 / 1024)), dedupeKey: "lowDisk");
            await Task.Delay(TimeSpan.FromHours(1));
        }
    }

    public async ValueTask DisposeAsync()
    {
        Social.Stop();
        LauncherUpdates.ApplyOnExitIfReady();
        Discord.Dispose();
        await Bridge.DisposeAsync();
    }

    /// <summary>Der SocialService braucht den GameLauncher und umgekehrt; dieser Stellvertreter löst den Kreis.</summary>
    private sealed class SocialHookProxy : IGameLaunchHook
    {
        public IGameLaunchHook? Target { get; set; }

        void IGameLaunchHook.OnStarted(LaunchContext ctx, RunningProcess process) => Target?.OnStarted(ctx, process);

        void IGameLaunchHook.OnExited(LaunchContext ctx, RunningProcess process, int exitCode, bool crashed) =>
            Target?.OnExited(ctx, process, exitCode, crashed);
    }
}
