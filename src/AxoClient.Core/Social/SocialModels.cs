using AxoClient.Core.Common;

namespace AxoClient.Core.Social;

public enum Relation
{
    Friends,
    Pending,
    Incoming
}

public enum PresenceState
{
    Offline,
    Launcher,
    Ingame
}

public sealed record Friend(string Uuid, string Name, Relation Relation, PresenceState State, long? Since, string? Join);

public sealed record Me(string Uuid, string Name, string Role);

public sealed record ServiceNotification(string Id, string Kind, Dictionary<string, object?>? Data, long Created, bool Read);

public sealed record InboxSender(string Uuid, string Name);

public sealed record InboxItem(string Id, InboxSender Sender, string Kind, string Title, long Created, long Expires);

/// <summary>Antwort von <c>GET /v2/sync</c>: alles in einem Aufruf.</summary>
public sealed record SyncResponse(Me Me, List<Friend> Friends, List<ServiceNotification> Notifications, List<InboxItem> Inbox);

/// <summary>Ein Eintrag in der Glocke (vom Service oder lokal entstanden).</summary>
public sealed record NotificationItem(
    string Id,
    string Type,
    TextRef Text,
    DateTimeOffset Created,
    bool Read,
    Dictionary<string, object?>? Data = null,
    bool FromService = false);

/// <summary>Was ein Freund in einem Paket mitschicken darf. Nie Pfade, nie Java-Argumente.</summary>
public sealed class InstanceRecipe
{
    public string Name { get; set; } = "";
    public string Minecraft { get; set; } = "";
    public string Loader { get; set; } = "vanilla";
    public string? LoaderVersion { get; set; }
    public List<RecipeContent> Content { get; set; } = [];
    public List<RecipeServer> Servers { get; set; } = [];
    public string? Options { get; set; }

    /// <summary>Mod-Schutz des Absenders: <c>pack</c> oder <c>full</c> (fehlt = frei). Welche Mods zum Pack gehören, steht an den Inhalten.</summary>
    public string? ModLock { get; set; }
}

public sealed record RecipeContent(string Type, string Source, string ProjectId, string VersionId, bool Pack = false);

public sealed record RecipeServer(string Name, string Address);

public sealed record InvitePayload(string Address, string? InstanceName, string? Minecraft);
