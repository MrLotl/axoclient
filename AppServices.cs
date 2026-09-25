namespace AxoClient;

public sealed class AppServices : IDisposable
{
    private bool _saveFailed;

    public AppServices(IDialogService dialogs)
    {
        Dialogs = dialogs;
        Http = new HttpClient();
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        Settings = LauncherSettings.Load();
        Instances = new InstanceRegistry(Settings, SaveSettings);
        Accounts = new AccountService(Http);
        Modrinth = new ModrinthClient(Http);
        Installer = new GameInstaller(Http);
        Discord = new DiscordPresence();
        Games = new GameRunner(this);
        Axo = new AxoService(this);
        Capes = new ClientCapeService(Http, Accounts, Axo, () => Games.RunningInstances);
    }

    public HttpClient Http { get; }
    public IDialogService Dialogs { get; }
    public LauncherSettings Settings { get; }
    public InstanceRegistry Instances { get; }
    public AccountService Accounts { get; }
    public ClientCapeService Capes { get; }
    public ModrinthClient Modrinth { get; }
    public GameInstaller Installer { get; }
    public GameRunner Games { get; }
    public AxoService Axo { get; }
    public DiscordPresence Discord { get; }

    public ContentStore ContentOf(Installation inst) => new(inst, Modrinth);

    public void SaveSettings()
    {
        try
        {
            Settings.Save();
            _saveFailed = false;
        }
        catch (Exception ex)
        {
            if (_saveFailed)
            {
                ErrorReport.Log("Einstellungen speichern", ex);
                return;
            }
            _saveFailed = true;
            _ = Dialogs.ShowErrorAsync("Einstellungen konnten nicht gespeichert werden", ex,
                $"Betroffen ist \"{AppPaths.LauncherDir}\". Änderungen an Instanzen und Einstellungen gehen beim " +
                "Beenden verloren, solange das so bleibt.");
        }
    }

    public void Dispose() => Discord.Dispose();
}
