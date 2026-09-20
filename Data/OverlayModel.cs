using System.Text.Json;
using System.Text.Json.Nodes;

namespace McLauncher;

/// <summary>
/// Ein Overlay-Profil als JSON. Der Launcher bearbeitet Profile nicht inhaltlich (das geschieht im Spiel), er
/// verwaltet sie nur: Server zuordnen, kopieren, teilen, löschen.
/// </summary>
public sealed class OverlayProfile
{
    public JsonObject Root { get; }

    public OverlayProfile(JsonObject root) => Root = root;

    public static OverlayProfile Parse(string json)
    {
        try
        {
            return new OverlayProfile(JsonNode.Parse(json) as JsonObject ?? new JsonObject());
        }
        catch (JsonException)
        {
            return new OverlayProfile(new JsonObject());
        }
    }

    public string ToJson() => Root.ToJsonString();

    /// <summary>Server, auf denen das Profil automatisch gilt.</summary>
    public List<string> Servers
    {
        get => (Root["server"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList() ?? [];
        set => Root["server"] = new JsonArray(value.Where(s => !string.IsNullOrWhiteSpace(s)).Take(8)
            .Select(s => (JsonNode)JsonValue.Create(s.Trim())!).ToArray());
    }
}
