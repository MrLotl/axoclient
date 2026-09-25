using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AxoClient.Sharing;

public static class ShareKinds
{
    public const string Instance = "instance";
    public const string Overlay = "overlay";
    public const string Content = "content";
    public const string Server = "server";

    private const string FormatPrefix = "axoclient-";

    public static string FormatOf(string kind) => FormatPrefix + kind;

    public static string? KindOf(string? format) =>
        format != null && format.StartsWith(FormatPrefix) ? format[FormatPrefix.Length..] : null;

    public static string Label(string kind) => kind switch
    {
        Instance => "Instanz",
        Overlay => "Overlay",
        Content => "Inhalt",
        Server => "Server",
        _ => "Paket"
    };

    public static string Icon(string kind) => kind switch
    {
        Instance => "",
        Overlay => "",
        Content => "",
        Server => "",
        _ => ""
    };
}

public class ShareFormatException(string message) : Exception(message);

public static partial class ShareValidation
{
    public const int MaxJsonChars = 250_000;
    public const int MaxFileBytes = 1_000_000;
    public const int MaxContentEntries = 600;
    public const int MaxServers = 50;
    public const int MaxOptionsChars = 60_000;
    public const int MaxOverlayChars = 40_000;

    [GeneratedRegex(@"^[A-Za-z0-9]{4,16}$")]
    private static partial Regex ModrinthIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._+\-]{1,40}$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._\-\[\]:]{1,255}$")]
    private static partial Regex AddressPattern();

    public static bool IsModrinthId(string? value) => value != null && ModrinthIdPattern().IsMatch(value);
    public static bool IsVersion(string? value) => value != null && VersionPattern().IsMatch(value);
    public static bool IsServerAddress(string? value) => value != null && AddressPattern().IsMatch(value);
}

public static class ShareJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string Write(SharePayload payload) => JsonSerializer.Serialize(payload, payload.GetType(), Options);

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

    public static string? DetectKind(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("format", out var format)
                   && format.ValueKind == JsonValueKind.String
                ? ShareKinds.KindOf(format.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
