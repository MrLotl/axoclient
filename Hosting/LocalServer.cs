using System.Text.Json.Serialization;

namespace AxoClient.Hosting;

public enum ServerSoftware
{
    Vanilla,
    Paper,
    Fabric,
    Velocity
}

public class LocalServer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public ServerSoftware Software { get; set; } = ServerSoftware.Paper;
    public string Version { get; set; } = "";
    public int Port { get; set; } = 25565;
    public int MaxPlayers { get; set; } = 10;
    public int RamMb { get; set; } = 4096;
    public string Dir { get; set; } = "";
    public string? JarFile { get; set; }
    public string? InstalledVersion { get; set; }
    public ServerSoftware? InstalledSoftware { get; set; }
    public bool Imported { get; set; }
    public bool OpenToInternet { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore] public bool IsProxy => Software == ServerSoftware.Velocity;
    [JsonIgnore] public string Address => $"localhost:{Port}";
    [JsonIgnore] public string LogsDir => Path.Combine(Dir, "logs");
    [JsonIgnore] public string LatestLog => Path.Combine(LogsDir, "latest.log");
    [JsonIgnore] public string IconFile => Path.Combine(Dir, "server-icon.png");
    [JsonIgnore] public string ModsDir => Path.Combine(Dir, "mods");
    [JsonIgnore] public string PluginsDir => Path.Combine(Dir, "plugins");

    [JsonIgnore]
    public string TypeLabel
    {
        get
        {
            var note = Software switch
            {
                ServerSoftware.Velocity => "Proxy",
                ServerSoftware.Fabric when FileOps.CountEntries(ModsDir, "*.jar") is > 0 and var mods => $"{mods} Mods",
                ServerSoftware.Paper when FileOps.CountEntries(PluginsDir, "*.jar") is > 0 and var plugins => $"{plugins} Plugins",
                _ => ""
            };
            return $"{Software} {Version}" + (note.Length > 0 ? " · " + note : "");
        }
    }

    public static string SoftwareHint(ServerSoftware software) => software switch
    {
        ServerSoftware.Vanilla => "Offizieller Minecraft-Server ohne Plugins oder Mods",
        ServerSoftware.Paper => "Schnell und unterstützt Bukkit/Spigot-Plugins",
        ServerSoftware.Fabric => "Für Server mit Fabric-Mods",
        _ => "Proxy, der mehrere Server miteinander verbindet"
    };
}

public sealed class LocalServerStore
{
    private static string IndexPath => Path.Combine(AppPaths.LocalServers, "servers.json");

    public List<LocalServer> Load() => JsonFiles.Read<List<LocalServer>>(IndexPath) ?? [];

    public void Save(IEnumerable<LocalServer> servers) => JsonFiles.Write(IndexPath, servers.ToList());

    public static string NewDir(string name, string id) =>
        Path.Combine(AppPaths.LocalServers, Sanitize.FileName(name, "Server") + "-" + id);
}
