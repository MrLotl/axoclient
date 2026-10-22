using System.Text.Json;
using AxoClient.Core.Common;
using AxoClient.Core.Game;

namespace AxoClient.Core.Servers;

/// <summary>Eine Server-Software: Versionen, Download der Server-Jar, benötigte Java-Version.</summary>
public interface IServerSoftware
{
    ServerSoftware Type { get; }

    /// <summary>Proxy statt Minecraft-Server (Velocity: keine EULA, keine Welten).</summary>
    bool IsProxy => false;

    Task<IReadOnlyList<ServerVersionInfo>> ListVersionsAsync(CancellationToken ct);

    Task<DownloadRequest> ResolveJarAsync(string version, string targetPath, CancellationToken ct);

    int RequiredJava(string version) => JavaManager.GuessMajor(version) is var j && j == 16 ? 17 : j;
}

public sealed class VanillaServer(ApiClient api, VersionManager versions) : IServerSoftware
{
    public ServerSoftware Type => ServerSoftware.Vanilla;

    public async Task<IReadOnlyList<ServerVersionInfo>> ListVersionsAsync(CancellationToken ct) =>
        (await versions.ListAsync(ct)).Where(v => v.Type != VersionType.Old)
        .Select(v => new ServerVersionInfo(v.Id, v.Type == VersionType.Release)).ToList();

    public async Task<DownloadRequest> ResolveJarAsync(string version, string targetPath, CancellationToken ct)
    {
        using var manifest = JsonDocument.Parse(await api.GetStringAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", TimeSpan.FromHours(1), ct));
        var entry = manifest.RootElement.GetProperty("versions").EnumerateArray()
            .FirstOrDefault(v => v.GetProperty("id").GetString() == version);
        if (entry.ValueKind != JsonValueKind.Object)
            throw new AxoException("servers.versionUnknown", new() { ["version"] = version });
        using var details = JsonDocument.Parse(await api.GetStringAsync(entry.GetProperty("url").GetString()!, TimeSpan.FromDays(7), ct));
        if (!details.RootElement.GetProperty("downloads").TryGetProperty("server", out var server))
            throw new AxoException("servers.noOfficialServer", new() { ["version"] = version });
        return new DownloadRequest(server.GetProperty("url").GetString()!, targetPath, server.GetProperty("sha1").GetString(),
            Size: server.GetProperty("size").GetInt64());
    }
}

/// <summary>Paper und Velocity über die Fill-API (v3) von PaperMC.</summary>
public abstract class PaperProjectServer(ApiClient api, string project) : IServerSoftware
{
    private const string Fill = "https://fill.papermc.io/v3/projects/";

    public abstract ServerSoftware Type { get; }

    public async Task<IReadOnlyList<ServerVersionInfo>> ListVersionsAsync(CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await api.GetStringAsync(Fill + project, TimeSpan.FromHours(1), ct));
        var versions = json.RootElement.GetProperty("versions");
        var ids = versions.ValueKind == JsonValueKind.Object
            ? versions.EnumerateObject().SelectMany(g => g.Value.EnumerateArray().Select(v => v.GetString()!)).ToList()
            : versions.EnumerateArray().Select(v => v.GetString()!).Reverse().ToList();
        return ids.Select(id => new ServerVersionInfo(id, Type == ServerSoftware.Velocity
                                                          || !(id.Contains("-pre", StringComparison.OrdinalIgnoreCase)
                                                               || id.Contains("-rc", StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    public async Task<DownloadRequest> ResolveJarAsync(string version, string targetPath, CancellationToken ct)
    {
        using var build = JsonDocument.Parse(await api.GetStringAsync(
            $"{Fill}{project}/versions/{Uri.EscapeDataString(version)}/builds/latest", ct: ct));
        var downloads = build.RootElement.GetProperty("downloads");
        var server = downloads.TryGetProperty("server:default", out var main) ? main : downloads.EnumerateObject().First().Value;
        var checksums = server.TryGetProperty("checksums", out var c) ? c : default;
        return new DownloadRequest(server.GetProperty("url").GetString()!, targetPath,
            Sha256: checksums.ValueKind == JsonValueKind.Object && checksums.TryGetProperty("sha256", out var sha) ? sha.GetString() : null,
            Size: server.TryGetProperty("size", out var size) ? size.GetInt64() : null);
    }
}

public sealed class PaperServer(ApiClient api) : PaperProjectServer(api, "paper")
{
    public override ServerSoftware Type => ServerSoftware.Paper;
}

public sealed class VelocityServer(ApiClient api) : PaperProjectServer(api, "velocity"), IServerSoftware
{
    public override ServerSoftware Type => ServerSoftware.Velocity;
    bool IServerSoftware.IsProxy => true;
    int IServerSoftware.RequiredJava(string version) => 21;
}

/// <summary>Fabric-Server über den offiziellen Server-Launcher von Fabric (eine Jar).</summary>
public sealed class FabricServer(ApiClient api) : IServerSoftware
{
    private const string Meta = "https://meta.fabricmc.net/v2/versions/";

    public ServerSoftware Type => ServerSoftware.Fabric;

    public async Task<IReadOnlyList<ServerVersionInfo>> ListVersionsAsync(CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await api.GetStringAsync(Meta + "game", TimeSpan.FromHours(1), ct));
        return json.RootElement.EnumerateArray()
            .Select(v => new ServerVersionInfo(v.GetProperty("version").GetString()!, v.GetProperty("stable").GetBoolean())).ToList();
    }

    public async Task<DownloadRequest> ResolveJarAsync(string version, string targetPath, CancellationToken ct)
    {
        using var loaders = JsonDocument.Parse(await api.GetStringAsync(Meta + "loader", TimeSpan.FromHours(6), ct));
        var loader = loaders.RootElement.EnumerateArray().First(l => l.GetProperty("stable").GetBoolean()).GetProperty("version").GetString();
        using var installers = JsonDocument.Parse(await api.GetStringAsync(Meta + "installer", TimeSpan.FromHours(6), ct));
        var installer = installers.RootElement.EnumerateArray().First(i => i.GetProperty("stable").GetBoolean()).GetProperty("version").GetString();
        // Fabric liefert für die Server-Jar keinen Hash; HTTPS von meta.fabricmc.net ist die Absicherung
        return new DownloadRequest($"{Meta}loader/{Uri.EscapeDataString(version)}/{loader}/{installer}/server/jar", targetPath);
    }
}
