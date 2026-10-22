using System.Text.Json.Serialization;

namespace AxoClient.Core.Common;

public enum AfterLaunch
{
    Open,
    Minimize,
    Hide,
    Close
}

public enum WindowMode
{
    Window,
    Maximized,
    Fullscreen
}

public enum UpdateChannel
{
    Stable,
    Beta
}

public enum FriendOnServerScope
{
    Active,
    All
}

public sealed record Size2(int W, int H);

public sealed class GameSettings
{
    public WindowMode WindowMode { get; set; } = WindowMode.Window;
    public Size2 WindowSize { get; set; } = new(1280, 720);
    public bool CheckModsOnLaunch { get; set; } = true;
}

public sealed class DefaultSettings
{
    public int MemoryMb { get; set; } = 6144;

    /// <summary><c>auto</c> oder ein Pfad zu javaw.exe.</summary>
    public string Java { get; set; } = "auto";
}

public sealed class IntegrationSettings
{
    public bool DiscordRpc { get; set; } = true;
    public bool FriendJoinRelay { get; set; } = true;
    public bool TablistIcon { get; set; } = true;
    public bool StreamerMode { get; set; }
}

public sealed class SocialSettings
{
    public bool ShowOnline { get; set; } = true;
    public bool AllowJoin { get; set; } = true;
}

public sealed record AccentGradient(string From, string To, int Angle);

public sealed class AppearanceSettings
{
    /// <summary><c>axo</c>, <c>mint</c>, <c>sunset</c>, <c>midnight</c> oder <c>custom</c>.</summary>
    public string Preset { get; set; } = "axo";
    public AccentGradient Accent { get; set; } = new("#db2777", "#7c3aed", 135);

    /// <summary>Skin auf der Startseite winkt einmal nach dem Start des Launchers.</summary>
    public bool WaveOnStart { get; set; } = true;
}

public sealed class NotificationTypes
{
    public bool FriendRequest { get; set; } = true;
    public bool FriendAccepted { get; set; } = true;
    public bool FriendOnline { get; set; }
    public bool FriendOnServer { get; set; } = true;
    public bool FriendOpenWorld { get; set; } = true;
    public bool Invite { get; set; } = true;
    public bool Inbox { get; set; } = true;
    public bool ModUpdates { get; set; } = true;
    public bool ModpackUpdates { get; set; } = true;
    public bool CapeReview { get; set; } = true;
    public bool LauncherUpdate { get; set; } = true;
    public bool JobFinished { get; set; } = true;
    public bool AuthExpired { get; set; } = true;
    public bool LowDiskSpace { get; set; } = true;
    public bool ServerCrashed { get; set; } = true;
    public bool ServerPlayerJoined { get; set; }

    public bool IsEnabled(string type) => type switch
    {
        "friendRequest" => FriendRequest,
        "friendAccepted" => FriendAccepted,
        "friendOnline" => FriendOnline,
        "friendOnServer" => FriendOnServer,
        "friendOpenWorld" => FriendOpenWorld,
        "invite" => Invite,
        "inbox" => Inbox,
        "modUpdates" => ModUpdates,
        "modpackUpdates" => ModpackUpdates,
        "capeReview" => CapeReview,
        "launcherUpdate" => LauncherUpdate,
        "jobFinished" => JobFinished,
        "authExpired" => AuthExpired,
        "lowDiskSpace" => LowDiskSpace,
        "serverCrashed" => ServerCrashed,
        "serverPlayerJoined" => ServerPlayerJoined,
        _ => true
    };
}

public sealed class NotificationSettings
{
    public bool WindowsToasts { get; set; } = true;
    public bool QuietWhilePlaying { get; set; } = true;
    public bool InvitesWhilePlaying { get; set; } = true;
    public FriendOnServerScope FriendOnServerScope { get; set; } = FriendOnServerScope.Active;
    public NotificationTypes Types { get; set; } = new();
}

public sealed class UpdateSettings
{
    public bool Auto { get; set; }
    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;
}

/// <summary><c>launcher.json</c>: alle Einstellungen des Launchers.</summary>
public sealed class LauncherSettings : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public bool Autostart { get; set; }
    public AfterLaunch AfterLaunch { get; set; } = AfterLaunch.Minimize;
    public GameSettings Game { get; set; } = new();
    public DefaultSettings Defaults { get; set; } = new();
    public IntegrationSettings Integrations { get; set; } = new();
    public SocialSettings Social { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();

    /// <summary>Wurde der Erst-Start-Assistent abgeschlossen?</summary>
    public bool SetupDone { get; set; }
}

public sealed class ViewState
{
    public string Instances { get; set; } = "grid";
    public string LocalServers { get; set; } = "list";
    public string Modpacks { get; set; } = "grid";
}

public sealed class SortState
{
    public string Modpacks { get; set; } = "popular";
    public string AddMods { get; set; } = "popular";
}

public sealed class SourceState
{
    public string AddMods { get; set; } = "modrinth";
    public string Modpacks { get; set; } = "modrinth";
}

/// <summary><c>state.json</c>: was sich der Launcher zwischen zwei Starts merkt (keine Einstellungen).</summary>
public sealed class UiState : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public string? LastInstance { get; set; }
    public ViewState Views { get; set; } = new();
    public SortState Sort { get; set; } = new();
    public SourceState ContentSource { get; set; } = new();

    [JsonIgnore]
    public static readonly string[] ViewModes = ["grid", "list"];
}

/// <summary>Hält launcher.json und state.json im Speicher und speichert Änderungen.</summary>
public sealed class SettingsService(AppPaths paths, EventHub events)
{
    private readonly JsonStore<LauncherSettings> _launcher = new(paths.Fs, paths.LauncherJson);
    private readonly JsonStore<UiState> _state = new(paths.Fs, paths.StateJson);

    public LauncherSettings Current { get; private set; } = new();
    public UiState State { get; private set; } = new();

    public event Action<LauncherSettings>? Changed;

    public async Task LoadAsync()
    {
        Current = await _launcher.LoadAsync();
        State = await _state.LoadAsync();
    }

    public async Task SaveAsync(LauncherSettings settings)
    {
        Validate(settings);
        await _launcher.SaveAsync(settings);
        Current = settings;
        Changed?.Invoke(settings);
        events.Publish("settings.changed", settings);
    }

    public async Task UpdateAsync(Action<LauncherSettings> change)
    {
        var copy = Json.Deserialize<LauncherSettings>(Json.Serialize(Current))!;
        change(copy);
        await SaveAsync(copy);
    }

    public async Task UpdateStateAsync(Action<UiState> change)
    {
        State = await _state.UpdateAsync(change);
    }

    private static void Validate(LauncherSettings s)
    {
        s.Defaults.MemoryMb = Math.Clamp(s.Defaults.MemoryMb, 512, 65536);
        s.Game.WindowSize = new Size2(Math.Clamp(s.Game.WindowSize.W, 320, 7680), Math.Clamp(s.Game.WindowSize.H, 240, 4320));
        if (s.Defaults.Java != "auto" && !(s.Defaults.Java.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase)
                                          || s.Defaults.Java.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase)))
            s.Defaults.Java = "auto";
        if (!Appearance.IsColor(s.Appearance.Accent.From) || !Appearance.IsColor(s.Appearance.Accent.To))
            s.Appearance = new AppearanceSettings();
        s.Appearance.Accent = s.Appearance.Accent with { Angle = Math.Clamp(s.Appearance.Accent.Angle, 0, 360) };
    }
}
