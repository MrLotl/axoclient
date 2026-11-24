using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.DataModel.Servers;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Servers.Contract;

namespace AxoClient.Servers
{
    /// <summary>Lokale Server: anlegen, bearbeiten, starten/stoppen, Konsole, Spielerliste und Absturzanalyse.</summary>
    public sealed partial class LocalServerManager : ILocalServerManager
    {
        private readonly AppPaths _paths;
        private readonly IServerJava _serverJava;
        private readonly IDownloadManager _downloads;
        private readonly IProcessRunner _processes;
        private readonly IJobManager _jobs;
        private readonly IActivityTracker _activity;
        private readonly ICrashAnalyzer _crashAnalyzer;
        private readonly IPortForwarding _upnp;
        private readonly IEventHub _events;
        private readonly ITextHelper _textHelper;
        private readonly IPathGuard _pathGuard;
        private readonly IAppLog _appLog;
        private readonly IImageFormats _imageFormats;
        private readonly IServerImport _serverImport;
        private readonly IGameVersionHelper _gameVersionHelper;
        private readonly IJsonStoreFactory _jsonStoreFactory;
        private readonly Dictionary<ServerSoftware, IServerSoftware> _softwares;
        private readonly ConcurrentDictionary<string, HashSet<string>> _players = new();
        private readonly ConcurrentDictionary<string, PortMapping> _mappings = new();
        private readonly ConcurrentDictionary<string, TextRef> _internetProblems = new();

        public LocalServerManager(
            AppPaths paths,
            IEnumerable<IServerSoftware> softwares,
            IServerJava serverJava,
            IDownloadManager downloads,
            IProcessRunner processes,
            IJobManager jobs,
            IActivityTracker activity,
            ICrashAnalyzer crashAnalyzer,
            IPortForwarding upnp,
            IEventHub events,
            ITextHelper textHelper,
            IPathGuard pathGuard,
            IAppLog appLog,
            IImageFormats imageFormats,
            IServerImport serverImport,
            IGameVersionHelper gameVersionHelper,
            IJsonStoreFactory jsonStoreFactory)
        {
            _paths = paths;
            _serverJava = serverJava;
            _downloads = downloads;
            _processes = processes;
            _jobs = jobs;
            _activity = activity;
            _crashAnalyzer = crashAnalyzer;
            _upnp = upnp;
            _events = events;
            _textHelper = textHelper;
            _pathGuard = pathGuard;
            _appLog = appLog;
            _imageFormats = imageFormats;
            _serverImport = serverImport;
            _gameVersionHelper = gameVersionHelper;
            _jsonStoreFactory = jsonStoreFactory;
            _softwares = softwares.ToDictionary(s => s.Type);
        }

        public string ProcessKey(string id) => "server:" + id;

        private JsonStore<LocalServer> store(string id) => _jsonStoreFactory.Create<LocalServer>(_paths.Fs, _paths.ServerJson(_textHelper.RequireId(id)));

        public IServerSoftware Software(ServerSoftware type) => _softwares[type];

        public Task<IReadOnlyList<ServerVersionInfo>> Versions(ServerSoftware type, CancellationToken ct) =>
            _softwares[type].ListVersions(ct);

        public async Task<IReadOnlyList<ServerView>> List(CancellationToken ct = default)
        {
            if (!Directory.Exists(_paths.Servers))
            {
                return [];
            }

            var views = new List<ServerView>();
            foreach (var dir in Directory.EnumerateDirectories(_paths.Servers))
            {
                var id = Path.GetFileName(dir);
                if (!_textHelper.IsValidId(id) || !File.Exists(_paths.ServerJson(id)))
                {
                    continue;
                }

                views.Add(await view(await store(id).Load(ct), ct));
            }
            return views.OrderByDescending(v => v.Running).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<LocalServer> Get(string id, CancellationToken ct = default)
        {
            if (!File.Exists(_paths.ServerJson(_textHelper.RequireId(id))))
            {
                throw new AxoException("servers.notFound");
            }

            var server = await store(id).Load(ct);
            server.Id = id;
            return server;
        }

        private async Task<ServerView> view(LocalServer server, CancellationToken ct)
        {
            var running = _processes.Get(ProcessKey(server.Id));
            long? memory = null;
            if (running != null)
            {
                try
                {
                    using var process = Process.GetProcessById(running.Pid);
                    memory = process.WorkingSet64 / 1024 / 1024;
                }
                catch (ArgumentException)
                {
                }
            }
            var uptime = await _activity.Load(_paths.UptimeJson(server.Id));
            var mods = server.Software == ServerSoftware.Fabric
                ? Directory.Exists(Path.Combine(_paths.ServerData(server.Id), "mods"))
                    ? Directory.EnumerateFiles(Path.Combine(_paths.ServerData(server.Id), "mods"), "*.jar").Count()
                    : 0
                : (int?)null;
            var players = _players.TryGetValue(server.Id, out var set) && running != null ? set.Order().ToList() : [];
            return new ServerView(server.Id, server.Name, server.Software, server.Version, server.Port, server.MaxPlayers,
                server.MemoryMb, server.OpenToInternet, server.EulaAccepted, running != null, players.Count, players, memory,
                running?.StartedAt,
                _mappings.TryGetValue(server.Id, out var mapping) ? $"{mapping.PublicIp}:{mapping.Port}" : null,
                _internetProblems.GetValueOrDefault(server.Id),
                $"/axo/server-icon-local/{server.Id}?v={iconVersion(server.Id)}", bannerUrl(server.Id), mods, uptime.TotalSeconds);
        }

        private long iconVersion(string id)
        {
            var icon = Path.Combine(_paths.ServerData(id), "server-icon.png");
            return File.Exists(icon) ? File.GetLastWriteTimeUtc(icon).Ticks % 1_000_000_007 : 0;
        }

        public string? IconPath(string id)
        {
            if (!_textHelper.IsValidId(id))
            {
                return null;
            }

            var icon = Path.Combine(_paths.ServerData(id), "server-icon.png");
            return File.Exists(icon) ? icon : null;
        }

        public string? BannerPath(string id)
        {
            if (!_textHelper.IsValidId(id))
            {
                return null;
            }

            var banner = Path.Combine(_paths.Server(id), "banner.img");
            return File.Exists(banner) ? banner : null;
        }

        private string? bannerUrl(string id)
        {
            var banner = BannerPath(id);
            return banner == null ? null : $"/axo/server-banner-local/{id}?v={File.GetLastWriteTimeUtc(banner).Ticks % 1_000_000_007}";
        }

        /// <summary>
        /// Servericon (<c>server-icon.png</c> im Serverordner, muss für Minecraft genau 64 × 64 PNG sein) oder Banner
        /// (nur für den Launcher). <paramref name="data"/> null entfernt das Bild.
        /// </summary>
        public async Task SetImage(string id, bool banner, byte[]? data, CancellationToken ct = default)
        {
            await Get(id, ct);
            var path = banner ? Path.Combine(_paths.Server(id), "banner.img") : Path.Combine(_paths.ServerData(id), "server-icon.png");
            if (data == null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            else
            {
                if (data.Length > _imageFormats.MaxImageBytes || !_imageFormats.IsPngOrJpeg(data))
                {
                    throw new AxoException("errors.invalidImage");
                }

                if (!banner && _imageFormats.PngSize(data) != (64, 64))
                {
                    throw new AxoException("localServers.iconSize");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, data, ct);
            }
            _events.Publish("servers.changed");
        }

        public async Task<LocalServer> Create(ServerRequest request, CancellationToken ct = default)
        {
            var server = validate(new LocalServer { CreatedAt = DateTimeOffset.UtcNow }, request);
            var id = _textHelper.NewId(server.Name);
            while (Directory.Exists(_paths.Server(id)))
            {
                id = _textHelper.NewId(server.Name);
            }

            server.Id = id;
            Directory.CreateDirectory(_paths.ServerData(id));
            await store(id).Save(server, ct);
            _events.Publish("servers.changed");
            return server;
        }

        /// <summary>Neuen Server anlegen und den Inhalt eines vorhandenen Server-Ordners hineinkopieren.</summary>
        public async Task<LocalServer> Import(ServerRequest request, string folder, JobContext job)
        {
            var server = await Create(request, job.Token);
            try
            {
                job.Status("localServers.import.copying");
                var failed = await Task.Run(() => _serverImport.CopyInto(folder, _paths.ServerData(server.Id), job), job.Token);
                if (failed > 0)
                {
                    _appLog.Warn($"Server-Import \"{server.Name}\": {failed} Dateien nicht kopiert");
                }
            }
            catch
            {
                await Delete(server.Id);
                throw;
            }
            _events.Publish("servers.changed");
            return server;
        }

        public async Task<LocalServer> Update(string id, ServerRequest request, CancellationToken ct = default)
        {
            var server = validate(await Get(id, ct), request);
            await store(id).Save(server, ct);
            _events.Publish("servers.changed");
            return server;
        }

        private LocalServer validate(LocalServer server, ServerRequest r)
        {
            server.Name = _textHelper.Clean(r.Name, 60) is { Length: > 0 } n ? n : throw new AxoException("servers.nameMissing");
            if (!_gameVersionHelper.IsSafeVersion(r.Version))
            {
                throw new AxoException("servers.versionMissing");
            }

            server.Software = r.Software;
            server.Version = r.Version;
            server.Port = r.Port is >= 1024 and <= 65535 ? r.Port : throw new AxoException("servers.portInvalid");
            server.MaxPlayers = Math.Clamp(r.MaxPlayers, 1, 500);
            server.MemoryMb = Math.Clamp(r.MemoryMb, 512, 32768);
            server.OpenToInternet = r.OpenToInternet;
            server.EulaAccepted = r.EulaAccepted;
            return server;
        }

        /// <summary>Nächster freier Port ab 25565 (für "Server erstellen").</summary>
        public async Task<int> NextFreePort(CancellationToken ct = default)
        {
            var used = (await List(ct)).Select(s => s.Port).ToHashSet();
            var port = 25565;
            while (used.Contains(port))
            {
                port++;
            }

            return port;
        }

        public async Task Delete(string id, CancellationToken ct = default)
        {
            if (_processes.Get(ProcessKey(id)) != null)
            {
                throw new AxoException("servers.running");
            }

            var dir = _paths.Server(_textHelper.RequireId(id));
            if (Directory.Exists(dir))
            {
                await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin), ct);
            }

            _events.Publish("servers.changed");
        }

        public Task Start(string id)
        {
            if (_jobs.IsRunning("server.start", id))
            {
                throw new AxoException("servers.alreadyStarting");
            }

            return _jobs.Run("server.start", new TextRef("servers.starting"), async job =>
            {
                var server = await Get(id, job.Token);
                var software = _softwares[server.Software];
                if (!software.IsProxy && !server.EulaAccepted)
                {
                    throw new AxoException("servers.eulaMissing");
                }

                if (_processes.Get(ProcessKey(id)) != null)
                {
                    return;
                }

                var data = _paths.ServerData(id);
                Directory.CreateDirectory(data);
                var jarName = $"{server.Software.ToString().ToLowerInvariant()}-{server.Version}.jar";
                var jar = Path.Combine(_paths.Server(id), jarName);
                if (server.InstalledFor != $"{server.Software}:{server.Version}" || !File.Exists(jar))
                {
                    job.Status("servers.downloadingJar");
                    var request = await software.ResolveJar(server.Version, jar, job.Token);
                    await _downloads.Download(request, job.Range(0, 0.6), job.Token);
                    foreach (var old in Directory.EnumerateFiles(_paths.Server(id), "*.jar").Where(f => f != jar))
                    {
                        File.Delete(old);
                    }

                    server.InstalledJar = jarName;
                    server.InstalledFor = $"{server.Software}:{server.Version}";
                    await store(id).Save(server, job.Token);
                }

                var javaExe = await _serverJava.Ensure(software.RequiredJava(server.Version), job.Sub(0.6, 0.9));
                await writeConfig(server, software, job.Token);

                var info = new ProcessStartInfo(javaExe) { WorkingDirectory = data };
                info.ArgumentList.Add($"-Xmx{server.MemoryMb}M");
                info.ArgumentList.Add($"-Xms{Math.Min(1024, server.MemoryMb)}M");
                info.ArgumentList.Add("-jar");
                info.ArgumentList.Add(jar);
                if (!software.IsProxy)
                {
                    info.ArgumentList.Add("nogui");
                }

                _players[id] = [];
                var running = _processes.Start(ProcessKey(id), info);
                running.LineReceived += line => onLine(server, line);
                _ = watch(server, running);

                if (server.OpenToInternet)
                {
                    _ = openPort(server);
                }

                _events.Publish("servers.changed");
                job.Report(1);
            }, id);
        }

        public async Task Stop(string id)
        {
            var running = _processes.Get(ProcessKey(id));
            if (running == null)
            {
                return;
            }

            var server = await Get(id);
            await running.Stop(_softwares[server.Software].IsProxy ? "end" : "stop", TimeSpan.FromSeconds(40));
        }

        public async Task SendCommand(string id, string command)
        {
            var running = _processes.Get(ProcessKey(id)) ?? throw new AxoException("servers.notRunning");
            var clean = _textHelper.Clean(command, 500).TrimStart('/');
            if (clean.Length > 0)
            {
                await running.WriteLine(clean);
            }
        }

        public IReadOnlyList<LogLine> LogOf(string id, int max = 5000) =>
            _processes.GetIncludingExited(ProcessKey(id))?.Lines(max) ?? [];

        private async Task writeConfig(LocalServer server, IServerSoftware software, CancellationToken ct)
        {
            var data = _paths.ServerData(server.Id);
            if (software.IsProxy)
            {
                // Velocity schreibt velocity.toml beim ersten Start selbst; danach nur den Port setzen
                var toml = Path.Combine(data, "velocity.toml");
                if (File.Exists(toml))
                {
                    var lines = (await File.ReadAllLinesAsync(toml, ct))
                        .Select(l => l.TrimStart().StartsWith("bind =") ? $"bind = \"0.0.0.0:{server.Port}\"" : l);
                    await File.WriteAllLinesAsync(toml, lines, ct);
                }
                return;
            }
            await File.WriteAllTextAsync(Path.Combine(data, "eula.txt"),
                "# Akzeptiert im AxoClient (https://aka.ms/MinecraftEULA)\neula=true\n", ct);
            var propsFile = Path.Combine(data, "server.properties");
            var props = File.Exists(propsFile) ? (await File.ReadAllLinesAsync(propsFile, ct)).ToList() : [];
            setProperty(props, "server-port", server.Port.ToString());
            setProperty(props, "max-players", server.MaxPlayers.ToString());
            if (!props.Any(l => l.StartsWith("motd=")))
            {
                setProperty(props, "motd", server.Name.Replace("\\", ""));
            }

            await File.WriteAllLinesAsync(propsFile, props, ct);
        }

        private void setProperty(List<string> lines, string key, string value)
        {
            var index = lines.FindIndex(l => l.StartsWith(key + "="));
            if (index >= 0)
            {
                lines[index] = $"{key}={value}";
            }
            else
            {
                lines.Add($"{key}={value}");
            }
        }

        private void onLine(LocalServer server, LogLine line)
        {
            if (joinedRegex().Match(line.Text) is { Success: true } joined)
            {
                var name = joined.Groups[1].Value;
                _players.GetOrAdd(server.Id, _ => []).Add(name);
                _events.Publish("notify", new
                {
                    type = "serverPlayerJoined",
                    text = new TextRef("notifications.serverPlayerJoined", ("player", name), ("server", server.Name)),
                    serverId = server.Id
                });
                _events.Publish("servers.changed");
            }
            else if (leftRegex().Match(line.Text) is { Success: true } left)
            {
                if (_players.TryGetValue(server.Id, out var set))
                {
                    set.Remove(left.Groups[1].Value);
                }

                _events.Publish("servers.changed");
            }
            _events.Publish("console.line", new { key = ProcessKey(server.Id), line });
        }

        private async Task watch(LocalServer server, RunningProcess running)
        {
            var exit = await running.Exited;
            await _activity.AddSession(_paths.UptimeJson(server.Id), ActivitySessionKind.Uptime, running.StartedAt, DateTimeOffset.Now);
            if (_mappings.TryRemove(server.Id, out var mapping))
            {
                await _upnp.Close(mapping);
            }

            _internetProblems.TryRemove(server.Id, out _);
            _players.TryRemove(server.Id, out _);
            _events.Publish("servers.changed");

            if (running.StopRequested || exit == 0)
            {
                return;
            }

            var analysis = _crashAnalyzer.Analyze(new CrashInput(exit, null, null, running.Tail(), null), null, server: true);
            _events.Publish("notify", new
            {
                type = "serverCrashed",
                text = new TextRef("notifications.serverCrashed", ("server", server.Name)),
                serverId = server.Id
            });
            _events.Publish("server.crashed", new { serverId = server.Id, serverName = server.Name, analysis });
        }

        private async Task openPort(LocalServer server)
        {
            try
            {
                var mapping = await _upnp.Open(server.Port, $"AxoClient {server.Name}", CancellationToken.None);
                _mappings[server.Id] = mapping;
            }
            catch (AxoException ex)
            {
                _internetProblems[server.Id] = ex.Text;
            }
            _events.Publish("servers.changed");
        }

        /// <summary>Maßnahmen aus dem Server-Absturz-Dialog.</summary>
        public async Task RunCrashAction(string id, string action, string? file)
        {
            var server = await Get(id);
            switch (action)
            {
                case "acceptEula":
                    server.EulaAccepted = true;
                    break;
                case "changePort":
                    server.Port = await NextFreePort() is var p && p == server.Port ? p + 1 : p;
                    break;
                case "increaseRam":
                    server.MemoryMb = Math.Min(server.MemoryMb + 2048, 32768);
                    break;
                case "disablePlugin":
                    {
                        var plugins = Path.Combine(_paths.ServerData(id), "plugins");
                        var name = Path.GetFileName(file ?? "");
                        var path = _pathGuard.SafeCombine(plugins, name.EndsWith(".jar") ? name : name + ".jar");
                        if (path != null && File.Exists(path))
                        {
                            File.Move(path, path + ".disabled", overwrite: true);
                        }

                        break;
                    }
                default:
                    throw new AxoException("errors.invalidInput");
            }
            await store(id).Save(server);
            _events.Publish("servers.changed");
        }

        public bool AnyRunning => _processes.AnyRunning(k => k.StartsWith("server:"));

        [GeneratedRegex(@"\]: ([A-Za-z0-9_]{3,16}) joined the game")]
        private partial Regex joinedRegex();

        [GeneratedRegex(@"\]: ([A-Za-z0-9_]{3,16}) left the game")]
        private partial Regex leftRegex();
    }
}
