using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;

namespace McLauncher;

public record CapeInfo(string Id, string Alias, bool Active);

public class ProfileInfo
{
    public byte[]? SkinPng { get; init; }
    public bool SkinSlim { get; init; }
    public BitmapSource? SkinFront { get; init; }
    public BitmapSource? Head { get; init; }
    public List<CapeInfo> Capes { get; init; } = [];
}

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string text, string confirmText = "OK", bool danger = false);
    Task ShowMessageAsync(string title, string text);

    Task<bool> ShowFormAsync(string title, System.Windows.FrameworkElement content, string confirmText,
        Func<bool>? validate = null);
}

/// <summary>Gemeinsamer Zustand aller Seiten: Einstellungen, Konto, Profil und Dienste.</summary>
public class AppState
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Ordner vor der Umbenennung in AxoClient; wird beim ersten Start einmalig umgezogen.</summary>
    public static readonly string OldLauncherDir = Path.Combine(AppData, ".mclauncher");

    // Launcher-Ordner: gemeinsame Spieldateien, Einstellungen, Skins und Instanzen
    public static readonly string LauncherDir = ResolveLauncherDir();

    /// <summary>
    /// Zieht ".mclauncher" nach ".axoclient" um (auf demselben Laufwerk nur ein Umbenennen, dauert nicht lange).
    /// Klappt das nicht, z.B. weil gerade ein Spiel Dateien geöffnet hat, wird vorerst der alte Ordner genutzt.
    /// </summary>
    private static string ResolveLauncherDir()
    {
        var dir = Path.Combine(AppData, ".axoclient");
        if (Directory.Exists(dir) || !Directory.Exists(OldLauncherDir))
            return dir;
        try
        {
            Directory.Move(OldLauncherDir, dir);
            return dir;
        }
        catch
        {
            return OldLauncherDir;
        }
    }

    private const string ProfileApi = "https://api.minecraftservices.com/minecraft/profile";

    public HttpClient Http { get; } = new();
    public LauncherSettings Settings { get; } = LauncherSettings.Load(LauncherDir);
    public JELoginHandler LoginHandler { get; } = JELoginHandlerBuilder.BuildDefault();
    public GameInstaller Installer { get; }
    public IDialogService Dialogs { get; }
    public DiscordPresence Discord { get; } = new();
    public FriendsService Friends { get; }

    public MSession? Session { get; private set; }
    public ProfileInfo? Profile { get; private set; }

    /// <summary>Anmeldung oder Profil (Skin, Umhang) hat sich geändert.</summary>
    public event Action? AccountChanged;

    /// <summary>Instanzen wurden angelegt, geändert, gelöscht oder eine andere ausgewählt.</summary>
    public event Action? InstallationsChanged;

    public AppState(IDialogService dialogs)
    {
        Dialogs = dialogs;
        Installer = new GameInstaller(LauncherDir, Http);
        Friends = new FriendsService(this);
    }

    public void Save() => Settings.Save(LauncherDir);

    public Installation? SelectedInstallation =>
        Settings.Installations.FirstOrDefault(i => i.Id == Settings.SelectedInstallationId)
        ?? Settings.Installations.FirstOrDefault();

    public void SelectInstallation(Installation inst)
    {
        Settings.SelectedInstallationId = inst.Id;
        Save();
        InstallationsChanged?.Invoke();
    }

    public void NotifyInstallationsChanged()
    {
        Save();
        InstallationsChanged?.Invoke();
    }

    // ---------- Laufende Spiele ----------

    private readonly Dictionary<string, System.Diagnostics.Process> _runningGames = new();

    /// <summary>Ein Spiel wurde gestartet oder beendet (im UI-Thread).</summary>
    public event Action? RunningGamesChanged;

    public bool IsRunning(Installation inst)
    {
        lock (_runningGames)
            return _runningGames.TryGetValue(inst.Id, out var process) && !process.HasExited;
    }

    /// <summary>Merkt sich das gestartete Spiel, damit es angezeigt und beendet werden kann.</summary>
    public void TrackGame(Installation inst, System.Diagnostics.Process process)
    {
        lock (_runningGames)
            _runningGames[inst.Id] = process;
        process.Exited += (_, _) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            lock (_runningGames)
                if (_runningGames.TryGetValue(inst.Id, out var tracked) && tracked == process)
                    _runningGames.Remove(inst.Id);
            RunningGamesChanged?.Invoke();
        });
        RunningGamesChanged?.Invoke();
    }

    /// <summary>Beendet ein laufendes Spiel nach Rückfrage sofort.</summary>
    public async Task StopGameAsync(Installation inst)
    {
        System.Diagnostics.Process? process;
        lock (_runningGames)
            _runningGames.TryGetValue(inst.Id, out process);
        if (process == null || process.HasExited)
            return;
        if (!await Dialogs.ConfirmAsync("Minecraft beenden",
                $"\"{inst.Name}\" sofort beenden?\n\nMinecraft speichert Welten alle paar Minuten automatisch; " +
                "was seitdem passiert ist, kann verloren gehen.", "Beenden", danger: true))
            return;
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // schon beendet
        }
    }

    // ---------- Konto ----------

    public async Task<bool> TryRestoreSessionAsync()
    {
        try
        {
            Session = await LoginHandler.AuthenticateSilently();
            await RefreshProfileAsync();
            return true;
        }
        catch
        {
            Session = null;
            AccountChanged?.Invoke();
            return false;
        }
    }

    public async Task LoginAsync()
    {
        Session = await LoginHandler.AuthenticateInteractively();
        await RefreshProfileAsync();
    }

    public async Task LogoutAsync()
    {
        await LoginHandler.Signout();
        Session = null;
        Profile = null;
        AccountChanged?.Invoke();
    }

    /// <summary>Frischt das Token auf (es läuft nach einiger Zeit ab) und liefert die Sitzung.</summary>
    public async Task<MSession> GetFreshSessionAsync()
    {
        Session = await LoginHandler.AuthenticateSilently();
        return Session;
    }

    // ---------- Profil: Skin & Umhang ----------

    public async Task RefreshProfileAsync()
    {
        try
        {
            using var json = await SendProfileRequestAsync(HttpMethod.Get, "");
            Profile = ParseProfile(json, await DownloadActiveSkinAsync(json));
        }
        catch
        {
            Profile = null; // Name wird trotzdem angezeigt, nur ohne Skin
        }
        AccountChanged?.Invoke();
    }

    public async Task UploadSkinAsync(byte[] png, bool slim)
    {
        await GetFreshSessionAsync();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(slim ? "slim" : "classic"), "variant");
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "skin.png");
        using var _ = await SendProfileRequestAsync(HttpMethod.Post, "/skins", form);
        await RefreshProfileAsync();
    }

    public async Task SetCapeAsync(string? capeId)
    {
        await GetFreshSessionAsync();
        HttpContent? body = capeId == null
            ? null
            : new StringContent(JsonSerializer.Serialize(new { capeId }), System.Text.Encoding.UTF8, "application/json");
        using var _ = await SendProfileRequestAsync(capeId == null ? HttpMethod.Delete : HttpMethod.Put,
            "/capes/active", body);
        await RefreshProfileAsync();
    }

    private async Task<JsonDocument> SendProfileRequestAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, ProfileApi + path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session!.AccessToken);
        using var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Minecraft-Dienste antworten mit {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private async Task<byte[]?> DownloadActiveSkinAsync(JsonDocument json)
    {
        if (!json.RootElement.TryGetProperty("skins", out var skins))
            return null;
        var active = skins.EnumerateArray().FirstOrDefault(s => s.GetProperty("state").GetString() == "ACTIVE");
        return active.ValueKind == JsonValueKind.Object
            ? await Http.GetByteArrayAsync(active.GetProperty("url").GetString())
            : null;
    }

    private static ProfileInfo ParseProfile(JsonDocument json, byte[]? skinPng)
    {
        var root = json.RootElement;
        var slim = root.TryGetProperty("skins", out var skins) && skins.EnumerateArray().Any(s =>
            s.GetProperty("state").GetString() == "ACTIVE"
            && s.TryGetProperty("variant", out var v) && v.GetString() == "SLIM");

        var capes = root.TryGetProperty("capes", out var capeArray)
            ? capeArray.EnumerateArray().Select(c => new CapeInfo(
                c.GetProperty("id").GetString()!,
                c.TryGetProperty("alias", out var a) ? a.GetString() ?? "Umhang" : "Umhang",
                c.GetProperty("state").GetString() == "ACTIVE")).ToList()
            : [];

        return new ProfileInfo
        {
            SkinPng = skinPng,
            SkinSlim = slim,
            SkinFront = skinPng != null ? SkinRenderer.RenderFront(skinPng, slim) : null,
            Head = skinPng != null ? SkinRenderer.RenderHead(skinPng) : null,
            Capes = capes
        };
    }
}
