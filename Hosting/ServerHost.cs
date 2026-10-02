using System.Diagnostics;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.Hosting;

public sealed class RunningServer(Process process)
{
    public Process Process { get; } = process;
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public int Online { get; set; }
    public int Max { get; set; }
    public bool Reachable { get; set; }
    public long RamBytes { get; set; }
    public bool Stopping { get; set; }
}

public sealed class ServerHost : IDisposable
{
    private const string JarName = "server.jar";

    private readonly HttpClient _http;
    private readonly LocalServerStore _store = new();
    private readonly Dictionary<string, RunningServer> _running = new();
    private readonly Timer _timer;
    private int _ticking;

    public ServerHost(HttpClient http)
    {
        _http = http;
        Servers = _store.Load();
        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    public List<LocalServer> Servers { get; }

    public event Action? Changed;

    public bool AnyRunning
    {
        get
        {
            lock (_running)
                return _running.Values.Any(r => !r.Process.HasExited);
        }
    }

    public RunningServer? StateOf(LocalServer server)
    {
        lock (_running)
            return _running.TryGetValue(server.Id, out var state) && !state.Process.HasExited ? state : null;
    }

    public bool IsRunning(LocalServer server) => StateOf(server) != null;

    public int NextPort()
    {
        var used = Servers.Select(s => s.Port).ToHashSet();
        var port = 25565;
        while (used.Contains(port))
            port++;
        return port;
    }

    private void Save() => _store.Save(Servers);

    public LocalServer Create(string name, ServerSoftware software, string version, int port, int maxPlayers, int ramMb, byte[]? icon)
    {
        var server = new LocalServer
        {
            Name = name,
            Software = software,
            Version = version,
            Port = port,
            MaxPlayers = maxPlayers,
            RamMb = ramMb
        };
        server.Dir = LocalServerStore.NewDir(name, server.Id);
        Directory.CreateDirectory(server.Dir);
        File.WriteAllText(Path.Combine(server.Dir, "eula.txt"),
            "# Akzeptiert in AxoClient (https://aka.ms/MinecraftEULA)\neula=true\n");
        if (icon != null)
            WriteIcon(server, icon);
        Servers.Add(server);
        Save();
        Changed?.Invoke();
        return server;
    }

    public void Update(LocalServer server, string name, ServerSoftware software, string version, int port, int maxPlayers,
        int ramMb, byte[]? icon, bool resetIcon)
    {
        server.Name = name;
        server.Software = software;
        server.Version = version;
        server.Port = port;
        server.MaxPlayers = maxPlayers;
        server.RamMb = ramMb;
        if (icon != null)
            WriteIcon(server, icon);
        else if (resetIcon)
            FileOps.TryDelete(server.IconFile);
        Save();
        Changed?.Invoke();
    }

    public LocalServer Import(string dir)
    {
        var jar = Directory.GetFiles(dir, "*.jar")
                      .OrderByDescending(f => Path.GetFileName(f).Contains("server", StringComparison.OrdinalIgnoreCase)
                                              || Path.GetFileName(f).Contains("paper", StringComparison.OrdinalIgnoreCase))
                      .FirstOrDefault()
                  ?? throw new InvalidOperationException("In diesem Ordner liegt keine Server-JAR-Datei.");
        var file = Path.GetFileName(jar).ToLowerInvariant();
        var software = file.Contains("paper") || File.Exists(Path.Combine(dir, "bukkit.yml")) ? ServerSoftware.Paper
            : file.Contains("velocity") || File.Exists(Path.Combine(dir, "velocity.toml")) ? ServerSoftware.Velocity
            : file.Contains("fabric") || Directory.Exists(Path.Combine(dir, ".fabric")) ? ServerSoftware.Fabric
            : ServerSoftware.Vanilla;
        var properties = ReadProperties(Path.Combine(dir, "server.properties"));
        var server = new LocalServer
        {
            Name = Path.GetFileName(dir.TrimEnd('\\', '/')),
            Software = software,
            Version = "",
            Dir = dir,
            JarFile = Path.GetFileName(jar),
            InstalledSoftware = software,
            InstalledVersion = "",
            Imported = true,
            Port = int.TryParse(properties.GetValueOrDefault("server-port"), out var port) ? port : NextPort(),
            MaxPlayers = int.TryParse(properties.GetValueOrDefault("max-players"), out var max) ? max : 20
        };
        Servers.Add(server);
        Save();
        Changed?.Invoke();
        return server;
    }

    public async Task DeleteAsync(LocalServer server, bool deleteFiles)
    {
        if (IsRunning(server))
            await StopAsync(server);
        Servers.Remove(server);
        Save();
        if (deleteFiles && !server.Imported && Directory.Exists(server.Dir))
            await Task.Run(() => FileOps.Recycle(server.Dir));
        Changed?.Invoke();
    }

    private static void WriteIcon(LocalServer server, byte[] image)
    {
        Directory.CreateDirectory(server.Dir);
        var source = Images.Decode(image);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            var scale = Math.Max(64.0 / source.PixelWidth, 64.0 / source.PixelHeight);
            double width = source.PixelWidth * scale, height = source.PixelHeight * scale;
            dc.DrawImage(source, new System.Windows.Rect((64 - width) / 2, (64 - height) / 2, width, height));
        }
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(server.IconFile);
        encoder.Save(stream);
    }

    public async Task PrepareAsync(LocalServer server, WorkProgress progress)
    {
        Directory.CreateDirectory(server.Dir);
        var jar = Path.Combine(server.Dir, server.JarFile ?? JarName);
        if (!server.Imported && (!File.Exists(jar) || server.InstalledVersion != server.Version
                                 || server.InstalledSoftware != server.Software))
        {
            progress.Text.Report($"Lade {server.Software} {server.Version} …");
            var url = await ServerDownloads.JarUrlAsync(_http, server.Software, server.Version);
            long done = 0;
            await HttpDownloads.DownloadToFileAsync(_http, url, Path.Combine(server.Dir, JarName), bytes =>
            {
                done += bytes;
                progress.Text.Report($"Lade {server.Software} {server.Version} … {done / 1048576.0:0.0} MB");
            }, progress.Cancel);
            server.JarFile = JarName;
            server.InstalledVersion = server.Version;
            server.InstalledSoftware = server.Software;
            Save();
        }

        if (server.IsProxy)
            WriteVelocityConfig(server);
        else
            WriteProperties(server);
    }

    private static Dictionary<string, string> ReadProperties(string path)
    {
        var result = new Dictionary<string, string>();
        if (!File.Exists(path))
            return result;
        foreach (var line in File.ReadAllLines(path))
        {
            var eq = line.IndexOf('=');
            if (eq > 0 && !line.StartsWith('#'))
                result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }

    private static void WriteProperties(LocalServer server)
    {
        var path = Path.Combine(server.Dir, "server.properties");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : ["#Minecraft server properties"];
        void Set(string key, string value)
        {
            var index = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
            if (index >= 0)
                lines[index] = key + "=" + value;
            else
                lines.Add(key + "=" + value);
        }
        Set("server-port", server.Port.ToString());
        Set("max-players", server.MaxPlayers.ToString());
        if (!lines.Any(l => l.StartsWith("motd=", StringComparison.Ordinal)))
            Set("motd", server.Name.Replace("\\", "").Replace("\n", " "));
        File.WriteAllLines(path, lines);
    }

    private static void WriteVelocityConfig(LocalServer server)
    {
        var path = Path.Combine(server.Dir, "velocity.toml");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, $"config-version = \"2.7\"\nbind = \"0.0.0.0:{server.Port}\"\nmotd = \"<#ec4899>{server.Name}\"\nshow-max-players = {server.MaxPlayers}\n");
            return;
        }
        var lines = File.ReadAllLines(path).ToList();
        var bind = lines.FindIndex(l => l.TrimStart().StartsWith("bind", StringComparison.Ordinal));
        if (bind >= 0)
            lines[bind] = $"bind = \"0.0.0.0:{server.Port}\"";
        var players = lines.FindIndex(l => l.TrimStart().StartsWith("show-max-players", StringComparison.Ordinal));
        if (players >= 0)
            lines[players] = $"show-max-players = {server.MaxPlayers}";
        File.WriteAllLines(path, lines);
    }

    public async Task StartAsync(LocalServer server, WorkProgress progress)
    {
        if (IsRunning(server))
            return;
        if (Servers.FirstOrDefault(s => s != server && s.Port == server.Port && IsRunning(s)) is { } clash)
            throw new InvalidOperationException($"Port {server.Port} benutzt schon „{clash.Name}“. Ändere den Port in den Einstellungen des Servers.");

        await PrepareAsync(server, progress);
        var required = await ServerDownloads.RequiredJavaAsync(_http, server.Software,
            server.Version.Length > 0 ? server.Version : "1.21.4");
        var java = await ServerJava.EnsureAsync(_http, required, progress);

        progress.Text.Report("Starte Server …");
        var info = new ProcessStartInfo(java)
        {
            WorkingDirectory = server.Dir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add($"-Xmx{server.RamMb}M");
        info.ArgumentList.Add($"-Xms{Math.Min(server.RamMb, 1024)}M");
        info.ArgumentList.Add("-Dfile.encoding=UTF-8");
        info.ArgumentList.Add("-jar");
        info.ArgumentList.Add(server.JarFile ?? JarName);
        if (!server.IsProxy)
            info.ArgumentList.Add("nogui");

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.Exited += (_, _) => Changed?.Invoke();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_running)
            _running[server.Id] = new RunningServer(process) { Max = server.MaxPlayers };
        Changed?.Invoke();
    }

    public void SendCommand(LocalServer server, string command)
    {
        if (StateOf(server) is not { } state)
            return;
        try
        {
            state.Process.StandardInput.WriteLine(command);
            state.Process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Befehl an den Server senden", ex);
        }
    }

    public async Task StopAsync(LocalServer server)
    {
        if (StateOf(server) is not { } state)
            return;
        state.Stopping = true;
        Changed?.Invoke();
        SendCommand(server, server.IsProxy ? "shutdown" : "stop");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            await state.Process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                state.Process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Server beenden", ex);
            }
        }
        lock (_running)
            _running.Remove(server.Id);
        Changed?.Invoke();
    }

    public async Task StopAllAsync()
    {
        var running = Servers.Where(IsRunning).ToList();
        await Task.WhenAll(running.Select(StopAsync));
    }

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
            return;
        try
        {
            List<(LocalServer Server, RunningServer State)> active;
            lock (_running)
                active = Servers.Where(s => _running.ContainsKey(s.Id)).Select(s => (s, _running[s.Id])).ToList();
            var changed = false;
            foreach (var (server, state) in active)
            {
                if (state.Process.HasExited)
                {
                    lock (_running)
                        _running.Remove(server.Id);
                    changed = true;
                    continue;
                }
                try
                {
                    state.Process.Refresh();
                    state.RamBytes = state.Process.WorkingSet64;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
                try
                {
                    var ping = await ServerPing.PingAsync(server.Address, 1500);
                    state.Online = ping.Online;
                    state.Max = ping.Max;
                    state.Reachable = true;
                }
                catch
                {
                    state.Reachable = false;
                }
                changed = true;
            }
            if (changed)
                Changed?.Invoke();
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    public void Dispose() => _timer.Dispose();
}
