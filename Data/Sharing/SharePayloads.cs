using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace McLauncher;

/// <summary>
/// Die Arten von Dingen, die man Freunden schicken kann. Die Namen gehen so an den Dienst und stehen in den
/// Paketen; sie dürfen sich nicht mehr ändern.
/// </summary>
public static class ShareKinds
{
    public const string Instance = "instance";
    public const string Overlay = "overlay";
    public const string Content = "content";
    public const string Server = "server";

    public static string FormatOf(string kind) => "axoclient-" + kind;

    public static string Label(string kind) => kind switch
    {
        Instance => "Instanz",
        Overlay => "Overlay",
        Content => "Inhalt",
        Server => "Server",
        _ => "Paket"
    };

    /// <summary>Symbol aus der Segoe-Icon-Schrift.</summary>
    public static string Icon(string kind) => kind switch
    {
        Instance => "",
        Overlay => "",
        Content => "",
        Server => "",
        _ => ""
    };
}

/// <summary>Ein geteiltes Paket ist ungültig oder passt nicht. Der Text ist für den Nutzer gedacht.</summary>
public class ShareFormatException(string message) : Exception(message);

/// <summary>
/// Prüfen und Säubern von Text aus geteilten Paketen. Alles darin stammt von einem anderen Menschen (oder von
/// jemandem, der sich als solcher ausgibt) und wird nie ungeprüft in Adressen, Pfade oder Anzeigen übernommen.
/// </summary>
public static partial class ShareValidation
{
    /// <summary>Mehr als das nimmt auch der Dienst nicht an (siehe MAX_SHARE_CHARS im Worker).</summary>
    public const int MaxJsonChars = 250_000;

    public const int MaxContentEntries = 600;
    public const int MaxServers = 50;
    public const int MaxOptionsChars = 60_000;
    public const int MaxOverlayChars = 40_000;

    /// <summary>Modrinth-Kennungen sind kurze alphanumerische Zeichenfolgen. Sie stehen in Adressen, daher streng.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9]{4,16}$")]
    private static partial Regex ModrinthIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._+\-]{1,40}$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._\-\[\]:]{1,255}$")]
    private static partial Regex AddressPattern();

    public static bool IsModrinthId(string? value) => value != null && ModrinthIdPattern().IsMatch(value);
    public static bool IsVersion(string? value) => value != null && VersionPattern().IsMatch(value);
    public static bool IsServerAddress(string? value) => value != null && AddressPattern().IsMatch(value);

    /// <summary>Entfernt Steuerzeichen und Zeilenumbrüche, kürzt und trimmt.</summary>
    public static string CleanText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        foreach (var c in text)
        {
            if (!char.IsControl(c))
                builder.Append(c);
            if (builder.Length >= maxLength)
                break;
        }
        return builder.ToString().Trim();
    }
}

/// <summary>Gemeinsame Kopfzeilen aller geteilten Pakete: Format-Name und Versionsnummer des Formats.</summary>
public abstract class SharePayload
{
    public const int CurrentVersion = 1;

    public string Format { get; set; } = "";
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Prüft und säubert den Inhalt; wirft <see cref="ShareFormatException"/>, wenn er nicht brauchbar ist.</summary>
    public abstract void Validate();
}

/// <summary>Lesen und Schreiben der Pakete als JSON.</summary>
public static class ShareJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Enums nur als Text: eine Zahl aus einer fremden Datei soll nie einen unbekannten Wert ergeben
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string Write(SharePayload payload) => JsonSerializer.Serialize(payload, payload.GetType(), Options);

    /// <summary>Liest ein Paket der erwarteten Art und prüft es.</summary>
    public static T Read<T>(string json, string kind) where T : SharePayload
    {
        if (json.Length > ShareValidation.MaxJsonChars)
            throw new ShareFormatException("Die Datei ist zu groß für ein AxoClient-Paket.");

        T? payload;
        try
        {
            payload = JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new ShareFormatException("Die Datei ist kein gültiges AxoClient-Paket.");
        }

        if (payload == null || payload.Format != ShareKinds.FormatOf(kind))
            throw new ShareFormatException($"Das ist kein AxoClient-Paket für \"{ShareKinds.Label(kind)}\".");
        if (payload.Version > SharePayload.CurrentVersion)
            throw new ShareFormatException(
                "Dieses Paket stammt aus einer neueren AxoClient-Version. Bitte aktualisiere den Launcher.");
        payload.Validate();
        return payload;
    }

    /// <summary>Welche Art Paket eine Datei ist (aus ihrem Format-Namen), ohne sie ganz auszuwerten.</summary>
    public static string? DetectKind(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("format", out var format)
                && format.GetString() is { } name && name.StartsWith("axoclient-"))
                return name["axoclient-".Length..];
        }
        catch (JsonException)
        {
            // kein JSON
        }
        return null;
    }
}

// ================= Instanz =================

/// <summary>Ein Mod, Ressourcenpaket oder Shader in einer geteilten Instanz.</summary>
public class ManifestContent
{
    public ContentType Type { get; set; }
    public string ProjectId { get; set; } = "";

    /// <summary>Genau diese Version; fehlt sie oder gibt es sie nicht mehr, wird die neueste passende genommen.</summary>
    public string? VersionId { get; set; }

    public string Title { get; set; } = "";
    public string? VersionName { get; set; }

    /// <summary>Nur bei Mods sinnvoll: war der Mod abgeschaltet?</summary>
    public bool Enabled { get; set; } = true;
}

public class ManifestServer
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
}

/// <summary>
/// Eine ganze Instanz als Rezept: Version, Loader und die Liste der Inhalte als Modrinth-Kennungen. Dateien selbst
/// stehen nicht drin – der Launcher des Empfängers lädt alles selbst von Modrinth. So bleibt das Paket klein, und
/// es kann keine Adresse in einer Datei stehen, von der etwas geladen würde.
/// </summary>
public sealed class InstanceManifest : SharePayload
{
    public InstanceManifest()
    {
        Format = ShareKinds.FormatOf(ShareKinds.Instance);
    }

    public string Name { get; set; } = "";
    public string Minecraft { get; set; } = "";
    public LoaderType Loader { get; set; }
    public string? LoaderVersion { get; set; }
    public List<ManifestContent> Content { get; set; } = [];
    public List<ManifestServer> Servers { get; set; } = [];

    /// <summary>Inhalt der options.txt (ohne die zuletzt besuchte Serveradresse).</summary>
    public string? Options { get; set; }

    /// <summary>Einstellungen des AxoClient-Overlays (axoclient-hud.json).</summary>
    public JsonElement? Overlay { get; set; }

    public override void Validate()
    {
        Name = ShareValidation.CleanText(Name, 60);
        if (!Enum.IsDefined(Loader))
            throw new ShareFormatException("Unbekannter Mod-Loader in der Instanz.");
        if (!ShareValidation.IsVersion(Minecraft))
            throw new ShareFormatException("Die Minecraft-Version in der Instanz ist ungültig.");
        if (LoaderVersion != null && !GameInstaller.IsSafeLoaderVersion(LoaderVersion))
            LoaderVersion = null; // nicht schlimm, dann gilt die neueste

        // "null" als Listeneintrag ist in JSON möglich und darf nicht zu einem Absturz führen
        Content = (Content ?? []).Where(c => c != null).ToList();
        Servers = (Servers ?? []).Where(s => s != null).ToList();
        if (Content.Count > ShareValidation.MaxContentEntries)
            throw new ShareFormatException($"Die Instanz enthält zu viele Inhalte (mehr als {ShareValidation.MaxContentEntries}).");
        if (Servers.Count > ShareValidation.MaxServers)
            throw new ShareFormatException($"Die Instanz enthält zu viele Server (mehr als {ShareValidation.MaxServers}).");

        var seen = new HashSet<(ContentType, string)>();
        var cleaned = new List<ManifestContent>();
        foreach (var entry in Content)
        {
            if (!Enum.IsDefined(entry.Type) || !ShareValidation.IsModrinthId(entry.ProjectId))
                throw new ShareFormatException("Ein Eintrag in der Instanz hat keine gültige Modrinth-Kennung.");
            if (!seen.Add((entry.Type, entry.ProjectId)))
                continue; // doppelt aufgeführt
            if (entry.VersionId != null && !ShareValidation.IsModrinthId(entry.VersionId))
                entry.VersionId = null;
            entry.Title = ShareValidation.CleanText(entry.Title, 100);
            if (entry.Title.Length == 0)
                entry.Title = entry.ProjectId;
            entry.VersionName = entry.VersionName == null ? null : ShareValidation.CleanText(entry.VersionName, 60);
            cleaned.Add(entry);
        }
        Content = cleaned;

        Servers = Servers
            .Where(s => ShareValidation.IsServerAddress(s.Address?.Trim()))
            .Select(s => new ManifestServer
            {
                Name = ShareValidation.CleanText(s.Name, 64) is { Length: > 0 } name ? name : s.Address!.Trim(),
                Address = s.Address!.Trim()
            })
            .ToList();

        if (Options != null)
        {
            if (Options.Length > ShareValidation.MaxOptionsChars || Options.Contains('\0'))
                Options = null; // Einstellungen sind nur ein Zusatz
        }
        if (Overlay is { } overlay)
        {
            if (overlay.ValueKind != JsonValueKind.Object || overlay.GetRawText().Length > ShareValidation.MaxOverlayChars)
                Overlay = null;
        }
    }
}

// ================= Overlay =================

/// <summary>Nur die Overlay-Einstellungen (Anzeigen, Positionen, Farben) einer Instanz.</summary>
public sealed class OverlayPayload : SharePayload
{
    public OverlayPayload()
    {
        Format = ShareKinds.FormatOf(ShareKinds.Overlay);
    }

    /// <summary>Aus welcher Instanz sie stammen (nur zur Anzeige).</summary>
    public string Name { get; set; } = "";

    public JsonElement Config { get; set; }

    public override void Validate()
    {
        Name = ShareValidation.CleanText(Name, 60);
        if (Config.ValueKind != JsonValueKind.Object)
            throw new ShareFormatException("Die Overlay-Einstellungen sind leer oder beschädigt.");
        if (Config.GetRawText().Length > ShareValidation.MaxOverlayChars)
            throw new ShareFormatException("Die Overlay-Einstellungen sind ungewöhnlich groß.");
    }
}

// ================= Einzelner Inhalt =================

public enum SharedContentKind
{
    Mod,
    ResourcePack,
    Shader,
    Modpack
}

/// <summary>Ein einzelner Mod, ein Ressourcenpaket, ein Shader oder ein Modpack von Modrinth (als Empfehlung).</summary>
public sealed class ContentPayload : SharePayload
{
    public ContentPayload()
    {
        Format = ShareKinds.FormatOf(ShareKinds.Content);
    }

    public SharedContentKind Kind { get; set; }
    public string ProjectId { get; set; } = "";
    public string? VersionId { get; set; }
    public string Title { get; set; } = "";

    public string KindText => Kind switch
    {
        SharedContentKind.Mod => "Mod",
        SharedContentKind.ResourcePack => "Ressourcenpaket",
        SharedContentKind.Shader => "Shader",
        _ => "Modpack"
    };

    /// <summary>Der Typ im Launcher; null bei Modpacks (die werden als neue Instanz installiert).</summary>
    public ContentType? InstallType => Kind switch
    {
        SharedContentKind.Mod => McLauncher.ContentType.Mod,
        SharedContentKind.ResourcePack => McLauncher.ContentType.ResourcePack,
        SharedContentKind.Shader => McLauncher.ContentType.Shader,
        _ => null
    };

    public override void Validate()
    {
        if (!Enum.IsDefined(Kind) || !ShareValidation.IsModrinthId(ProjectId))
            throw new ShareFormatException("Der geteilte Inhalt hat keine gültige Modrinth-Kennung.");
        if (VersionId != null && !ShareValidation.IsModrinthId(VersionId))
            VersionId = null;
        Title = ShareValidation.CleanText(Title, 100);
        if (Title.Length == 0)
            Title = ProjectId;
    }
}

// ================= Server =================

public sealed class ServerPayload : SharePayload
{
    public ServerPayload()
    {
        Format = ShareKinds.FormatOf(ShareKinds.Server);
    }

    public string Name { get; set; } = "";
    public string Address { get; set; } = "";

    public override void Validate()
    {
        Address = Address?.Trim() ?? "";
        if (!ShareValidation.IsServerAddress(Address))
            throw new ShareFormatException("Die Serveradresse ist ungültig.");
        Name = ShareValidation.CleanText(Name, 64);
        if (Name.Length == 0)
            Name = Address;
    }
}
