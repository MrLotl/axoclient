using System.IO.Abstractions;

namespace AxoClient.Core.Common;

/// <summary>Alle Pfade des Datenordners an einer Stelle (siehe docs/plan/02-ordnerstruktur.md).</summary>
public sealed class AppPaths
{
    public AppPaths(string root, IFileSystem? fs = null)
    {
        Fs = fs ?? new FileSystem();
        Root = Fs.Path.GetFullPath(root);
    }

    public static AppPaths Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AxoClient"));

    public IFileSystem Fs { get; }
    public string Root { get; }

    public string LauncherJson => Combine("launcher.json");
    public string AccountsJson => Combine("accounts.json");
    public string StateJson => Combine("state.json");
    public string TokensFile => Combine("tokens.dat");

    public string Instances => Combine("instances");
    public string Servers => Combine("servers");
    public string Skins => Combine("skins");
    public string SkinLibrary => Combine("skins", "library.json");
    public string Capes => Combine("capes");
    public string CapeLibrary => Combine("capes", "library.json");
    public string Emotes => Combine("emotes");
    public string EmoteLibrary => Combine("emotes", "library.json");

    public string Game => Combine("game");
    public string Versions => Combine("game", "versions");
    public string Libraries => Combine("game", "libraries");
    public string Assets => Combine("game", "assets");

    public string Java => Combine("java");
    public string Cache => Combine("cache");
    public string ApiCache => Combine("cache", "api");
    public string DownloadCache => Combine("cache", "downloads");
    public string Heads => Combine("cache", "heads");
    public string ServerIcons => Combine("cache", "server-icons");
    public string Updates => Combine("updates");
    public string Logs => Combine("logs");
    public string Tmp => Combine("tmp");

    public string Instance(string id) => Combine("instances", id);
    public string InstanceJson(string id) => Combine("instances", id, "instance.json");
    public string ContentJson(string id) => Combine("instances", id, "content.json");
    public string PlaytimeJson(string id) => Combine("instances", id, "playtime.json");
    public string PlacesJson(string id) => Combine("instances", id, "places.json");
    public string InstanceBackups(string id) => Combine("instances", id, "backups");
    public string GameDir(string id) => Combine("instances", id, "minecraft");

    public string Server(string id) => Combine("servers", id);
    public string ServerJson(string id) => Combine("servers", id, "server.json");
    public string UptimeJson(string id) => Combine("servers", id, "uptime.json");
    public string ServerData(string id) => Combine("servers", id, "data");

    /// <summary>Legt alle festen Ordner an. Fehlende Ordner sind nie ein Fehler.</summary>
    public void EnsureCreated()
    {
        foreach (var dir in new[]
                 {
                     Root, Instances, Servers, Skins, Capes, Emotes, Versions, Libraries, Assets, Java, ApiCache,
                     DownloadCache, Heads, ServerIcons, Updates, Logs, Tmp
                 })
            Fs.Directory.CreateDirectory(dir);
    }

    private string Combine(params string[] parts) => Fs.Path.Combine([Root, .. parts]);
}
