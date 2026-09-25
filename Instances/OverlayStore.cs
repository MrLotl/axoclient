using System.Text.Json;
using System.Text.Json.Nodes;

namespace AxoClient.Instances;

public sealed class OverlayProfile(JsonObject root)
{
    public JsonObject Root { get; } = root;

    public static OverlayProfile Parse(string json)
    {
        try
        {
            return new OverlayProfile(JsonNode.Parse(json) as JsonObject ?? new JsonObject());
        }
        catch (JsonException ex)
        {
            ErrorReport.Log("Overlay-Einstellungen lesen", ex);
            return new OverlayProfile(new JsonObject());
        }
    }

    public static OverlayProfile From(JsonElement element) => Parse(element.GetRawText());

    public string ToJson() => Root.ToJsonString();

    public JsonElement ToElement() => JsonDocument.Parse(ToJson()).RootElement.Clone();

    public List<string> Servers
    {
        get => (Root["server"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList() ?? [];
        set => Root["server"] = new JsonArray(value.Where(s => !string.IsNullOrWhiteSpace(s)).Take(8)
            .Select(s => (JsonNode)JsonValue.Create(s.Trim())!).ToArray());
    }

    public OverlayProfile Detached()
    {
        var copy = Parse(ToJson());
        copy.Root.Remove("server");
        copy.Root.Remove("profil");
        return copy;
    }

    public static int CountEnabled(JsonElement config) =>
        config.ValueKind != JsonValueKind.Object
            ? 0
            : config.EnumerateObject().Count(p => p.Value.ValueKind == JsonValueKind.Object
                                                  && p.Value.TryGetProperty("enabled", out var enabled)
                                                  && enabled.ValueKind == JsonValueKind.True);
}

public sealed class OverlayStore(Installation inst)
{
    public const string DefaultProfile = "Standard";
    private const int MaxProfileName = 24;
    private const string ConfigName = "axoclient-hud.json";
    private const string ProfilesName = "axoclient-hud-profiles";

    private string ConfigPath => Path.Combine(inst.ConfigDir, ConfigName);
    private string ProfilesDir => Path.Combine(inst.ConfigDir, ProfilesName);
    private string ProfilePath(string cleanName) => Path.Combine(ProfilesDir, cleanName + ".json");

    public static string CleanProfileName(string? name)
    {
        var clean = new string((name ?? "").Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        return clean.Length > MaxProfileName ? clean[..MaxProfileName].Trim() : clean;
    }

    public JsonElement? ReadActive() => JsonFiles.ReadObject(ConfigPath);

    public void WriteActive(JsonElement config)
    {
        Directory.CreateDirectory(inst.ConfigDir);
        if (File.Exists(ConfigPath))
            File.Copy(ConfigPath, ConfigPath + ".bak", overwrite: true);
        File.WriteAllText(ConfigPath, config.GetRawText());
    }

    public void EnsureDefaultProfile()
    {
        try
        {
            var path = ProfilePath(DefaultProfile);
            if (File.Exists(path))
                return;
            Directory.CreateDirectory(ProfilesDir);
            File.WriteAllText(path, ReadActive()?.GetRawText() ?? "{}");
        }
        catch (IOException ex)
        {
            ErrorReport.Log("Overlay-Standardprofil anlegen", ex);
        }
    }

    public string ActiveProfile()
    {
        if (ReadActive() is { } config && config.TryGetProperty("profil", out var name)
            && name.ValueKind == JsonValueKind.String && CleanProfileName(name.GetString()) is { Length: > 0 } clean)
            return clean;
        return DefaultProfile;
    }

    public bool IsActive(string name) => ActiveProfile().Equals(CleanProfileName(name), StringComparison.OrdinalIgnoreCase);

    public List<string> ProfileNames()
    {
        try
        {
            return Directory.Exists(ProfilesDir)
                ? Directory.GetFiles(ProfilesDir, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                    .OrderBy(n => n != DefaultProfile).ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList()
                : [];
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Overlay-Profile auflisten", ex);
            return [];
        }
    }

    public JsonElement? ReadProfile(string name)
    {
        var clean = CleanProfileName(name);
        return clean.Length == 0 ? null : JsonFiles.ReadObject(ProfilePath(clean));
    }

    public OverlayProfile? LoadProfile(string name) => ReadProfile(name) is { } json ? OverlayProfile.From(json) : null;

    public string WriteProfile(string name, OverlayProfile profile)
    {
        var clean = CleanProfileName(name);
        if (clean.Length == 0)
            clean = "Profil";
        Directory.CreateDirectory(ProfilesDir);
        File.WriteAllText(ProfilePath(clean), profile.ToJson());
        if (IsActive(clean))
            WriteActiveProfile(clean, profile);
        return clean;
    }

    public void Activate(string name)
    {
        var clean = CleanProfileName(name);
        if (LoadProfile(clean) is { } profile)
            WriteActiveProfile(clean, profile);
    }

    public bool DeleteProfile(string name)
    {
        var clean = CleanProfileName(name);
        if (clean.Equals(DefaultProfile, StringComparison.OrdinalIgnoreCase))
            return false;
        var wasActive = IsActive(clean);
        if (!FileOps.TryDelete(ProfilePath(clean)))
            return false;
        if (wasActive)
            Activate(DefaultProfile);
        return true;
    }

    public int CopyTo(Installation target)
    {
        try
        {
            if (!Directory.Exists(ProfilesDir))
                return 0;
            var targetStore = new OverlayStore(target);
            Directory.CreateDirectory(targetStore.ProfilesDir);
            var count = 0;
            foreach (var file in Directory.GetFiles(ProfilesDir, "*.json"))
            {
                File.Copy(file, Path.Combine(targetStore.ProfilesDir, Path.GetFileName(file)), overwrite: true);
                count++;
            }
            if (File.Exists(ConfigPath))
                File.Copy(ConfigPath, targetStore.ConfigPath, overwrite: true);
            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private void WriteActiveProfile(string name, OverlayProfile profile)
    {
        var copy = OverlayProfile.Parse(profile.ToJson());
        copy.Root["profil"] = name;
        Directory.CreateDirectory(inst.ConfigDir);
        File.WriteAllText(ConfigPath, copy.ToJson());
    }
}
