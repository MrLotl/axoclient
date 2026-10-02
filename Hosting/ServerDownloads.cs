using System.Text.Json;

namespace AxoClient.Hosting;

public static class ServerDownloads
{
    private const string MojangManifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string Fill = "https://fill.papermc.io/v3/projects/";
    private const string PaperV2 = "https://api.papermc.io/v2/projects/";
    private const string FabricMeta = "https://meta.fabricmc.net/v2/versions/";

    private static readonly Dictionary<ServerSoftware, List<string>> VersionCache = new();

    public static async Task<List<string>> VersionsAsync(HttpClient http, ServerSoftware software)
    {
        if (VersionCache.TryGetValue(software, out var cached))
            return cached;
        var versions = software switch
        {
            ServerSoftware.Vanilla => await VanillaVersionsAsync(http),
            ServerSoftware.Fabric => await FabricVersionsAsync(http),
            _ => await PaperProjectVersionsAsync(http, software == ServerSoftware.Paper ? "paper" : "velocity")
        };
        VersionCache[software] = versions;
        return versions;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string url) =>
        JsonDocument.Parse(await http.GetStringAsync(url));

    private static async Task<List<string>> VanillaVersionsAsync(HttpClient http)
    {
        using var json = await GetJsonAsync(http, MojangManifest);
        return json.RootElement.GetProperty("versions").EnumerateArray()
            .Where(v => v.GetProperty("type").GetString() == "release")
            .Select(v => v.GetProperty("id").GetString()!)
            .ToList();
    }

    private static async Task<List<string>> FabricVersionsAsync(HttpClient http)
    {
        using var json = await GetJsonAsync(http, FabricMeta + "game");
        return json.RootElement.EnumerateArray()
            .Where(v => v.GetProperty("stable").GetBoolean())
            .Select(v => v.GetProperty("version").GetString()!)
            .ToList();
    }

    private static async Task<List<string>> PaperProjectVersionsAsync(HttpClient http, string project)
    {
        try
        {
            using var json = await GetJsonAsync(http, Fill + project);
            var versions = json.RootElement.GetProperty("versions");
            return versions.ValueKind == JsonValueKind.Object
                ? versions.EnumerateObject().SelectMany(group => group.Value.EnumerateArray().Select(v => v.GetString()!)).ToList()
                : versions.EnumerateArray().Select(v => v.GetString()!).Reverse().ToList();
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"{project}-Versionen über Fill abfragen", ex);
            using var json = await GetJsonAsync(http, PaperV2 + project);
            return json.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!).Reverse().ToList();
        }
    }

    public static async Task<string> JarUrlAsync(HttpClient http, ServerSoftware software, string version)
    {
        switch (software)
        {
            case ServerSoftware.Vanilla:
            {
                using var manifest = await GetJsonAsync(http, MojangManifest);
                var entry = manifest.RootElement.GetProperty("versions").EnumerateArray()
                    .FirstOrDefault(v => v.GetProperty("id").GetString() == version);
                if (entry.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"Minecraft {version} gibt es nicht.");
                using var details = await GetJsonAsync(http, entry.GetProperty("url").GetString()!);
                if (!details.RootElement.GetProperty("downloads").TryGetProperty("server", out var server))
                    throw new InvalidOperationException($"Für Minecraft {version} gibt es keinen offiziellen Server.");
                return server.GetProperty("url").GetString()!;
            }
            case ServerSoftware.Fabric:
            {
                using var loaders = await GetJsonAsync(http, FabricMeta + "loader");
                var loader = loaders.RootElement.EnumerateArray().First(l => l.GetProperty("stable").GetBoolean())
                    .GetProperty("version").GetString();
                using var installers = await GetJsonAsync(http, FabricMeta + "installer");
                var installer = installers.RootElement.EnumerateArray().First(i => i.GetProperty("stable").GetBoolean())
                    .GetProperty("version").GetString();
                return $"{FabricMeta}loader/{Uri.EscapeDataString(version)}/{loader}/{installer}/server/jar";
            }
            default:
                return await PaperBuildUrlAsync(http, software == ServerSoftware.Paper ? "paper" : "velocity", version);
        }
    }

    private static async Task<string> PaperBuildUrlAsync(HttpClient http, string project, string version)
    {
        try
        {
            using var build = await GetJsonAsync(http, $"{Fill}{project}/versions/{Uri.EscapeDataString(version)}/builds/latest");
            var downloads = build.RootElement.GetProperty("downloads");
            var server = downloads.TryGetProperty("server:default", out var main) ? main : downloads.EnumerateObject().First().Value;
            return server.GetProperty("url").GetString()!;
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"{project}-Build über Fill abfragen", ex);
            using var builds = await GetJsonAsync(http, $"{PaperV2}{project}/versions/{Uri.EscapeDataString(version)}/builds");
            var latest = builds.RootElement.GetProperty("builds").EnumerateArray().Last();
            var number = latest.GetProperty("build").GetInt32();
            var name = latest.GetProperty("downloads").GetProperty("application").GetProperty("name").GetString();
            return $"{PaperV2}{project}/versions/{Uri.EscapeDataString(version)}/builds/{number}/downloads/{name}";
        }
    }

    public static async Task<int> RequiredJavaAsync(HttpClient http, ServerSoftware software, string version)
    {
        var fallback = RequiredJava(software, version);
        if (software == ServerSoftware.Velocity)
            return fallback;
        try
        {
            using var manifest = await GetJsonAsync(http, MojangManifest);
            var entry = manifest.RootElement.GetProperty("versions").EnumerateArray()
                .FirstOrDefault(v => v.GetProperty("id").GetString() == version);
            if (entry.ValueKind != JsonValueKind.Object)
                return fallback;
            using var details = await GetJsonAsync(http, entry.GetProperty("url").GetString()!);
            return details.RootElement.TryGetProperty("javaVersion", out var java)
                   && java.TryGetProperty("majorVersion", out var major) && major.TryGetInt32(out var value)
                ? Math.Max(value, fallback == 8 ? 8 : value)
                : fallback;
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Java-Version für den Server bestimmen", ex);
            return fallback;
        }
    }

    public static int RequiredJava(ServerSoftware software, string version)
    {
        if (software == ServerSoftware.Velocity)
            return 21;
        var parts = version.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major))
            return 21;
        if (major > 1)
            return 21;
        var minor = int.TryParse(parts[1], out var m) ? m : 21;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
        if (minor > 20 || (minor == 20 && patch >= 5))
            return 21;
        return minor >= 18 ? 17 : minor == 17 ? 16 : 8;
    }
}
