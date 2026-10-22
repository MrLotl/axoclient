using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AxoClient.Core.Common;
using AxoClient.Core.Launch;

namespace AxoClient.Core.Servers;

/// <summary>Lokale Server: anlegen, bearbeiten, starten/stoppen, Konsole, Spielerliste und Absturzanalyse.</summary>
public sealed partial class LocalServerManager(
    AppPaths paths,
    IEnumerable<IServerSoftware> softwares,
    ServerJava serverJava,
    DownloadManager downloads,
    ProcessRunner processes,
    JobManager jobs,
    ActivityTracker activity,
    CrashAnalyzer crashAnalyzer,
    PortForwarding upnp,
    EventHub events)
{
    private readonly Dictionary<ServerSoftware, IServerSoftware> _softwares = softwares.ToDictionary(s => s.Type);
    private readonly ConcurrentDictionary<string, HashSet<string>> _players = new();
    private readonly ConcurrentDictionary<string, PortMapping> _mappings = new();
    private readonly ConcurrentDictionary<string, TextRef> _internetProblems = new();

    public static string ProcessKey(string id) => "server:" + id;

    private JsonStore<LocalServer> Store(string id) => new(paths.Fs, paths.ServerJson(Text.RequireId(id)));

    public IServerSoftware Software(ServerSoftware type) => _softwares[type];

    public Task<IReadOnlyList<ServerVersionInfo>> VersionsAsync(ServerSoftware type, CancellationToken ct) =>
        _softwares[type].ListVersionsAsync(ct);

    public async Task<IReadOnlyList<ServerView>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(paths.Servers))
            return [];
        var views = new List<ServerView>();
        foreach (var dir in Directory.EnumerateDirectories(paths.Servers))
        {
            var id = Path.GetFileName(dir);
            if (!Text.IsValidId(id) || !File.Exists(paths.ServerJson(id)))
                continue;
            views.Add(await ViewAsync(await Store(id).LoadAsync(ct), ct));
        }
        return views.OrderByDescending(v => v.Running).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<LocalServer> GetAsync(string id, CancellationToken ct = default)
    {
        if (!File.Exists(paths.ServerJson(Text.RequireId(id))))
            throw new AxoException("servers.notFound");
        var server = await Store(id).LoadAsync(ct);
        server.Id = id;
        return server;
    }

    private async Task<ServerView> ViewAsync(LocalServer server, CancellationToken ct)
    {
        var running = processes.Get(ProcessKey(server.Id));
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
        var uptime = await activity.LoadAsync(paths.UptimeJson(server.Id));
        var mods = server.Software == ServerSoftware.Fabric
            ? Directory.Exists(Path.Combine(paths.ServerData(server.Id), "mods"))
                ? Directory.EnumerateFiles(Path.Combine(paths.ServerData(server.Id), "mods"), "*.jar").Count()
                : 0
            : (int?)null;
        var players = _players.TryGetValue(server.Id, out var set) && running != null ? set.Order().ToList() : [];
        return new ServerView(server.Id, server.Name, server.Software, server.Version, server.Port, server.MaxPlayers,
            server.MemoryMb, server.OpenToInternet, server.EulaAccepted, running != null, players.Count, players, memory,
            running?.StartedAt,
            _mappings.TryGetValue(server.Id, out var mapping) ? $"{mapping.PublicIp}:{mapping.Port}" : null,
            _internetProblems.GetValueOrDefault(server.Id),
            $"/axo/server-icon-local/{server.Id}?v={IconVersion(server.Id)}", BannerUrl(server.Id), mods, uptime.TotalSeconds);
    }

    private long IconVersion(string id)
    {
        var icon = Path.Combine(paths.ServerData(id), "server-icon.png");
        return File.Exists(icon) ? File.GetLastWriteTimeUtc(icon).Ticks % 1_000_000_007 : 0;
    }

    public string? IconPath(string id)
    {
        if (!Text.IsValidId(id))
            return null;
        var icon = Path.Combine(paths.ServerData(id), "server-icon.png");
        return File.Exists(icon) ? icon : null;
    }

    public string? BannerPath(string id)
    {
        if (!Text.IsValidId(id))
            return null;
        var banner = Path.Combine(paths.Server(id), "banner.img");
        return File.Exists(banner) ? banner : null;
    }

    private string? BannerUrl(string id)
    {
        var banner = BannerPath(id);
        return banner == null ? null : $"/axo/server-banner-local/{id}?v={File.GetLastWriteTimeUtc(banner).Ticks % 1_000_000_007}";
    }

    /// <summary>
    /// Servericon (<c>server-icon.png</c> im Serverordner, muss für Minecraft genau 64 × 64 PNG sein) oder Banner
    /// (nur für den Launcher). <paramref name="data"/> null entfernt das Bild.
    /// </summary>
    public async Task SetImageAsync(string id, bool banner, byte[]? data, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        var path = banner ? Path.Combine(paths.Server(id), "banner.img") : Path.Combine(paths.ServerData(id), "server-icon.png");
        if (data == null)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        else
        {
            if (data.Length > Instances.InstanceManager.MaxImageBytes || !Instances.ImageFormats.IsPngOrJpeg(data))
                throw new AxoException("errors.invalidImage");
            if (!banner && Instances.ImageFormats.PngSize(data) != (64, 64))
                throw new AxoException("localServers.iconSize");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, data, ct);
        }
        events.Publish("servers.changed");
    }

    public async Task<LocalServer> CreateAsync(ServerRequest request, CancellationToken ct = default)
    {
        var server = Validate(new LocalServer { CreatedAt = DateTimeOffset.UtcNow }, request);
        var id = Text.NewId(server.Name);
        while (Directory.Exists(paths.Server(id)))
            id = Text.NewId(server.Name);
        server.Id = id;
        Directory.CreateDirectory(paths.ServerData(id));
        await Store(id).SaveAsync(server, ct);
        events.Publish("servers.changed");
        return server;
    }

    /// <summary>Neuen Server anlegen und den Inhalt eines vorhandenen Server-Ordners hineinkopieren.</summary>
    public async Task<LocalServer> ImportAsync(ServerRequest request, string folder, JobContext job)
    {
        var server = await CreateAsync(request, job.Token);
        try
        {
            job.Status("localServers.import.copying");
            var failed = await Task.Run(() => ServerImport.CopyInto(folder, paths.ServerData(server.Id), job), job.Token);
            if (failed > 0)
                AppLog.Warn($"Server-Import \"{server.Name}\": {failed} Dateien nicht kopiert");
        }
        catch
        {
            await DeleteAsync(server.Id);
            throw;
        }
        events.Publish("servers.changed");
        return server;
    }

    public async Task<LocalServer> UpdateAsync(string id, ServerRequest request, CancellationToken ct = default)
    {
        var server = Validate(await GetAsync(id, ct), request);
        await Store(id).SaveAsync(server, ct);
        events.Publish("servers.changed");
        return server;
    }

    private static LocalServer Validate(LocalServer server, ServerRequest r)
    {
        server.Name = Text.Clean(r.Name, 60) is { Length: > 0 } n ? n : throw new AxoException("servers.nameMissing");
        if (!Game.LoaderManager.IsSafeVersion(r.Version))
            throw new AxoException("servers.versionMissing");
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
    public async Task<int> NextFreePortAsync(CancellationToken ct = default)
    {
        var used = (await ListAsync(ct)).Select(s => s.Port).ToHashSet();
        var port = 25565;
        while (used.Contains(port))
            port++;
        return port;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        if (processes.Get(ProcessKey(id)) != null)
            throw new AxoException("servers.running");
        var dir = paths.Server(Text.RequireId(id));
        if (Directory.Exists(dir))
            await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin), ct);
        events.Publish("servers.changed");
    }

    public Task StartAsync(string id)
    {
        if (jobs.IsRunning("server.start", id))
            throw new AxoException("servers.alreadyStarting");
        return jobs.RunAsync("server.start", new TextRef("servers.starting"), async job =>
        {
            var server = await GetAsync(id, job.Token);
            var software = _softwares[server.Software];
            if (!software.IsProxy && !server.EulaAccepted)
                throw new AxoException("servers.eulaMissing");
            if (processes.Get(ProcessKey(id)) != null)
                return;

            var data = paths.ServerData(id);
            Directory.CreateDirectory(data);
            var jarName = $"{server.Software.ToString().ToLowerInvariant()}-{server.Version}.jar";
            var jar = Path.Combine(paths.Server(id), jarName);
            if (server.InstalledFor != $"{server.Software}:{server.Version}" || !File.Exists(jar))
            {
                job.Status("servers.downloadingJar");
                var request = await software.ResolveJarAsync(server.Version, jar, job.Token);
                await downloads.DownloadAsync(request, job.Range(0, 0.6), job.Token);
                foreach (var old in Directory.EnumerateFiles(paths.Server(id), "*.jar").Where(f => f != jar))
                    File.Delete(old);
                server.InstalledJar = jarName;
                server.InstalledFor = $"{server.Software}:{server.Version}";
                await Store(id).SaveAsync(server, job.Token);
            }

            var javaExe = await serverJava.EnsureAsync(software.RequiredJava(server.Version), job.Sub(0.6, 0.9));
            await WriteConfigAsync(server, software, job.Token);

            var info = new ProcessStartInfo(javaExe) { WorkingDirectory = data };
            info.ArgumentList.Add($"-Xmx{server.MemoryMb}M");
            info.ArgumentList.Add($"-Xms{Math.Min(1024, server.MemoryMb)}M");
            info.ArgumentList.Add("-jar");
            info.ArgumentList.Add(jar);
            if (!software.IsProxy)
                info.ArgumentList.Add("nogui");

            _players[id] = [];
            var running = processes.Start(ProcessKey(id), info);
            running.LineReceived += line => OnLine(server, line);
            _ = WatchAsync(server, running);

            if (server.OpenToInternet)
                _ = OpenPortAsync(server);
            events.Publish("servers.changed");
            job.Report(1);
        }, id);
    }

    public async Task StopAsync(string id)
    {
        var running = processes.Get(ProcessKey(id));
        if (running == null)
            return;
        var server = await GetAsync(id);
        await running.StopAsync(_softwares[server.Software].IsProxy ? "end" : "stop", TimeSpan.FromSeconds(40));
    }

    public async Task SendCommandAsync(string id, string command)
    {
        var running = processes.Get(ProcessKey(id)) ?? throw new AxoException("servers.notRunning");
        var clean = Text.Clean(command, 500).TrimStart('/');
        if (clean.Length > 0)
            await running.WriteLineAsync(clean);
    }

    public IReadOnlyList<LogLine> LogOf(string id, int max = 5000) =>
        processes.GetIncludingExited(ProcessKey(id))?.Lines(max) ?? [];

    private async Task WriteConfigAsync(LocalServer server, IServerSoftware software, CancellationToken ct)
    {
        var data = paths.ServerData(server.Id);
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
        SetProperty(props, "server-port", server.Port.ToString());
        SetProperty(props, "max-players", server.MaxPlayers.ToString());
        if (!props.Any(l => l.StartsWith("motd=")))
            SetProperty(props, "motd", server.Name.Replace("\\", ""));
        await File.WriteAllLinesAsync(propsFile, props, ct);
    }

    private static void SetProperty(List<string> lines, string key, string value)
    {
        var index = lines.FindIndex(l => l.StartsWith(key + "="));
        if (index >= 0)
            lines[index] = $"{key}={value}";
        else
            lines.Add($"{key}={value}");
    }

    private void OnLine(LocalServer server, LogLine line)
    {
        if (JoinedRegex().Match(line.Text) is { Success: true } joined)
        {
            var name = joined.Groups[1].Value;
            _players.GetOrAdd(server.Id, _ => []).Add(name);
            events.Publish("notify", new
            {
                type = "serverPlayerJoined",
                text = TextRef.Of("notifications.serverPlayerJoined", ("player", name), ("server", server.Name)),
                serverId = server.Id
            });
            events.Publish("servers.changed");
        }
        else if (LeftRegex().Match(line.Text) is { Success: true } left)
        {
            if (_players.TryGetValue(server.Id, out var set))
                set.Remove(left.Groups[1].Value);
            events.Publish("servers.changed");
        }
        events.Publish("console.line", new { key = ProcessKey(server.Id), line });
    }

    private async Task WatchAsync(LocalServer server, RunningProcess running)
    {
        var exit = await running.Exited;
        await activity.AddSessionAsync(paths.UptimeJson(server.Id), ActivityTracker.Kind.Uptime, running.StartedAt, DateTimeOffset.Now);
        if (_mappings.TryRemove(server.Id, out var mapping))
            await upnp.CloseAsync(mapping);
        _internetProblems.TryRemove(server.Id, out _);
        _players.TryRemove(server.Id, out _);
        events.Publish("servers.changed");

        if (running.StopRequested || exit == 0)
            return;
        var analysis = crashAnalyzer.Analyze(new CrashInput(exit, null, null, running.Tail(), null), null, server: true);
        events.Publish("notify", new
        {
            type = "serverCrashed",
            text = TextRef.Of("notifications.serverCrashed", ("server", server.Name)),
            serverId = server.Id
        });
        events.Publish("server.crashed", new { serverId = server.Id, serverName = server.Name, analysis });
    }

    private async Task OpenPortAsync(LocalServer server)
    {
        try
        {
            var mapping = await upnp.OpenAsync(server.Port, $"AxoClient {server.Name}", CancellationToken.None);
            _mappings[server.Id] = mapping;
        }
        catch (AxoException ex)
        {
            _internetProblems[server.Id] = ex.Text;
        }
        events.Publish("servers.changed");
    }

    /// <summary>Maßnahmen aus dem Server-Absturz-Dialog.</summary>
    public async Task RunCrashActionAsync(string id, string action, string? file)
    {
        var server = await GetAsync(id);
        switch (action)
        {
            case "acceptEula":
                server.EulaAccepted = true;
                break;
            case "changePort":
                server.Port = await NextFreePortAsync() is var p && p == server.Port ? p + 1 : p;
                break;
            case "increaseRam":
                server.MemoryMb = Math.Min(server.MemoryMb + 2048, 32768);
                break;
            case "disablePlugin":
            {
                var plugins = Path.Combine(paths.ServerData(id), "plugins");
                var name = Path.GetFileName(file ?? "");
                var path = PathGuard.SafeCombine(plugins, name.EndsWith(".jar") ? name : name + ".jar");
                if (path != null && File.Exists(path))
                    File.Move(path, path + ".disabled", overwrite: true);
                break;
            }
            default:
                throw new AxoException("errors.invalidInput");
        }
        await Store(id).SaveAsync(server);
        events.Publish("servers.changed");
    }

    public bool AnyRunning => processes.AnyRunning(k => k.StartsWith("server:"));

    [GeneratedRegex(@"\]: ([A-Za-z0-9_]{3,16}) joined the game")]
    private static partial Regex JoinedRegex();

    [GeneratedRegex(@"\]: ([A-Za-z0-9_]{3,16}) left the game")]
    private static partial Regex LeftRegex();
}
