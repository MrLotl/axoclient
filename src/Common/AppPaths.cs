using System.IO.Abstractions;

namespace AxoClient.Common
{
    /// <summary>Alle Pfade des Datenordners an einer Stelle (siehe docs/plan/02-ordnerstruktur.md).</summary>
    public sealed class AppPaths
    {
        public AppPaths(string root, IFileSystem? fileSystem = null)
        {
            Fs = fileSystem ?? new FileSystem();
            Root = Fs.Path.GetFullPath(root);
        }

        /// <summary>Der übliche Datenordner <c>%AppData%\AxoClient</c>.</summary>
        public AppPaths()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AxoClient"))
        {
        }

        public IFileSystem Fs { get; }
        public string Root { get; }

        public string LauncherJson => combine("launcher.json");
        public string AccountsJson => combine("accounts.json");
        public string StateJson => combine("state.json");
        public string TokensFile => combine("tokens.dat");

        public string Instances => combine("instances");
        public string Servers => combine("servers");
        public string Skins => combine("skins");
        public string SkinLibrary => combine("skins", "library.json");
        public string Capes => combine("capes");
        public string CapeLibrary => combine("capes", "library.json");
        public string Emotes => combine("emotes");
        public string EmoteLibrary => combine("emotes", "library.json");

        public string Game => combine("game");
        public string Versions => combine("game", "versions");
        public string Libraries => combine("game", "libraries");
        public string Assets => combine("game", "assets");

        public string Java => combine("java");
        public string Cache => combine("cache");
        public string ApiCache => combine("cache", "api");
        public string DownloadCache => combine("cache", "downloads");
        public string Heads => combine("cache", "heads");
        public string ServerIcons => combine("cache", "server-icons");
        public string Updates => combine("updates");
        public string Logs => combine("logs");
        public string Tmp => combine("tmp");

        public string Instance(string id) => combine("instances", id);
        public string InstanceJson(string id) => combine("instances", id, "instance.json");
        public string ContentJson(string id) => combine("instances", id, "content.json");
        public string PlaytimeJson(string id) => combine("instances", id, "playtime.json");
        public string PlacesJson(string id) => combine("instances", id, "places.json");
        public string InstanceBackups(string id) => combine("instances", id, "backups");
        public string GameDirectory(string id) => combine("instances", id, "minecraft");

        public string Server(string id) => combine("servers", id);
        public string ServerJson(string id) => combine("servers", id, "server.json");
        public string UptimeJson(string id) => combine("servers", id, "uptime.json");
        public string ServerData(string id) => combine("servers", id, "data");

        /// <summary>Legt alle festen Ordner an. Fehlende Ordner sind nie ein Fehler.</summary>
        public void EnsureCreated()
        {
            foreach (var directory in new[]
                     {
                         Root, Instances, Servers, Skins, Capes, Emotes, Versions, Libraries, Assets, Java, ApiCache,
                         DownloadCache, Heads, ServerIcons, Updates, Logs, Tmp
                     })
            {
                Fs.Directory.CreateDirectory(directory);
            }
        }

        private string combine(params string[] parts) => Fs.Path.Combine([Root, .. parts]);
    }
}
