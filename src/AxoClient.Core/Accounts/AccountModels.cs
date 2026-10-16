using System.Text.Json.Serialization;
using AxoClient.Core.Common;

namespace AxoClient.Core.Accounts;

public enum AccountType
{
    Microsoft,
    Dev
}

public enum AxoCapeSource
{
    Official,
    Custom
}

public sealed record AxoCapeChoice(AxoCapeSource Source, string Id);

public sealed class AccountCapes
{
    /// <summary>ID des aktiven Mojang-Capes (nur zur Anzeige, die Wahrheit liegt bei Mojang).</summary>
    public string? Mojang { get; set; }
    public AxoCapeChoice? Axo { get; set; }
}

public sealed class AccountEntry
{
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; } = AccountType.Microsoft;

    /// <summary>ID des Skins aus der eigenen Bibliothek, falls einer gewählt wurde.</summary>
    public string? Skin { get; set; }
    public AccountCapes Capes { get; set; } = new();
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary><c>accounts.json</c>: nur Namen und Auswahl, Tokens liegen im <see cref="TokenStore"/>.</summary>
public sealed class AccountsDocument : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public string? Active { get; set; }
    public List<AccountEntry> Accounts { get; set; } = [];
}

/// <summary>Alles, was zum Starten des Spiels nötig ist. Wird nie gespeichert oder an die UI geschickt.</summary>
public sealed record GameSession(string Uuid, string Name, string AccessToken, string? Xuid, AccountType Type)
{
    [JsonIgnore]
    public string CompactUuid => Uuid.Replace("-", "").ToLowerInvariant();
}

/// <summary>Was die UI über einen Account sieht.</summary>
public sealed record AccountView(string Uuid, string Name, AccountType Type, bool Active, string HeadUrl, bool NeedsLogin);
