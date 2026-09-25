namespace AxoClient.Online;

public class FriendInfo
{
    public required string Uuid { get; init; }
    public required string Name { get; init; }
    public required string State { get; init; }
    public bool Playing { get; init; }
    public string? Server { get; init; }
    public string? Version { get; init; }

    public string HeadUrl => $"https://mc-heads.net/avatar/{Uuid}/64";
    public bool IsIncoming => State == "incoming";
    public bool IsFriend => State == "friend";
    public bool CanJoin => Playing && Server != null;

    public string StatusText => State switch
    {
        "incoming" => "Möchte dich als Freund hinzufügen",
        "outgoing" => "Anfrage gesendet",
        _ when Server != null => $"Spielt auf {Server}" + (Version != null ? $" · {Version}" : ""),
        _ when Playing => "Im Spiel (Menü oder Einzelspieler)",
        _ => "Nicht im Spiel"
    };

    public string RemoveText => State switch
    {
        "incoming" => "Ablehnen",
        "outgoing" => "Anfrage zurückziehen",
        _ => "Freund entfernen"
    };

    public int SortKey => CanJoin ? 0 : Playing ? 1 : IsIncoming ? 2 : IsFriend ? 3 : 4;
}

public class ShareInfo
{
    public required long Id { get; init; }
    public required string FromUuid { get; init; }
    public required string FromName { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public int Size { get; init; }
    public DateTime CreatedUtc { get; init; }

    public string HeadUrl => $"https://mc-heads.net/avatar/{FromUuid}/64";
    public string Icon => ShareKinds.Icon(Kind);
    public string Summary => $"{ShareKinds.Label(Kind)} von {FromName} · {Formats.Ago(CreatedUtc)}";
}
