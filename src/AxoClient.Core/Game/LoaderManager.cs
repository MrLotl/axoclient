using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AxoClient.Core.Common;
using CmlLib.Core;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.Installers;

namespace AxoClient.Core.Game;

/// <summary>Was ein Loader-Installer zum Arbeiten bekommt.</summary>
public sealed record LoaderInstallContext(
    MinecraftLauncher Launcher,
    MinecraftPath Path,
    string MinecraftVersion,
    string? LoaderVersion,
    IProgress<string> Status,
    IProgress<InstallerProgressChangedEventArgs> Files,
    IProgress<ByteProgress> Bytes,
    CancellationToken Token);

/// <summary>Ein Mod-Loader (Fabric, Forge, NeoForge, Quilt).</summary>
public interface ILoaderInstaller
{
    LoaderType Type { get; }

    Task<IReadOnlyList<LoaderVersionInfo>> ListVersionsAsync(string minecraftVersion, CancellationToken ct);

    /// <summary>Installiert den Loader und gibt die Versions-ID für CmlLib zurück.</summary>
    Task<string> InstallAsync(LoaderInstallContext ctx);

    /// <summary>Ist genau diese Loader-Version bereits installiert? (Für die Start-Pipeline.)</summary>
    bool IsInstalled(MinecraftPath path, string minecraftVersion, string? loaderVersion);
}

public sealed partial class LoaderManager(IEnumerable<ILoaderInstaller> installers)
{
    private readonly Dictionary<LoaderType, ILoaderInstaller> _installers = installers.ToDictionary(i => i.Type);

    public ILoaderInstaller? For(LoaderType type) => _installers.GetValueOrDefault(type);

    public Task<IReadOnlyList<LoaderVersionInfo>> ListVersionsAsync(LoaderType type, string mc, CancellationToken ct) =>
        For(type)?.ListVersionsAsync(mc, ct) ?? Task.FromResult<IReadOnlyList<LoaderVersionInfo>>([]);

    /// <summary>Gewählte oder empfohlene Loader-Version.</summary>
    public async Task<string?> ResolveVersionAsync(LoaderType type, string mc, string? wanted, CancellationToken ct)
    {
        if (type == LoaderType.Vanilla)
            return null;
        if (IsSafeVersion(wanted))
            return wanted;
        var versions = await ListVersionsAsync(type, mc, ct);
        return (versions.FirstOrDefault(v => v.Recommended) ?? versions.FirstOrDefault(v => v.Stable) ?? versions.FirstOrDefault())?.Version
               ?? throw new AxoException("game.loaderUnsupported",
                   new() { ["loader"] = LoaderNames.Display(type), ["version"] = mc });
    }

    public static bool IsSafeVersion(string? version) =>
        version is { Length: > 0 and <= 60 } && SafeVersion().IsMatch(version);

    [GeneratedRegex(@"^[A-Za-z0-9._+\-]+$")]
    private static partial Regex SafeVersion();
}

/// <summary>Fabric und Quilt schreiben ein Profil-JSON, den Rest lädt CmlLib.</summary>
public abstract class MetaProfileLoader(ApiClient api) : ILoaderInstaller
{
    public abstract LoaderType Type { get; }
    protected abstract string MetaUrl { get; }
    protected abstract string ProfilePrefix { get; }

    public async Task<IReadOnlyList<LoaderVersionInfo>> ListVersionsAsync(string mc, CancellationToken ct)
    {
        var text = await api.GetStringAsync($"{MetaUrl}/{Uri.EscapeDataString(mc)}", TimeSpan.FromHours(6), ct);
        using var doc = JsonDocument.Parse(text);
        var list = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("loader"))
            .Select(l => (Version: l.GetProperty("version").GetString()!,
                Stable: !l.TryGetProperty("stable", out var s) || s.GetBoolean()))
            .ToList();
        var recommended = PickRecommended(mc, list.Select(l => (l.Version, l.Stable)).ToList());
        return list.Select(l => new LoaderVersionInfo(l.Version, l.Stable && !l.Version.Contains('-'), l.Version == recommended)).ToList();
    }

    protected virtual string? PickRecommended(string mc, List<(string Version, bool Stable)> versions) =>
        versions.FirstOrDefault(v => v.Stable).Version ?? versions.FirstOrDefault().Version;

    public async Task<string> InstallAsync(LoaderInstallContext ctx)
    {
        var version = ctx.LoaderVersion;
        if (!LoaderManager.IsSafeVersion(version))
            version = (await ListVersionsAsync(ctx.MinecraftVersion, ctx.Token)).FirstOrDefault(v => v.Recommended)?.Version;
        if (version == null)
            throw new AxoException("game.loaderUnsupported",
                new() { ["loader"] = LoaderNames.Display(Type), ["version"] = ctx.MinecraftVersion });

        var id = ProfileId(ctx.MinecraftVersion, version);
        var jsonPath = ctx.Path.GetVersionJsonPath(id);
        if (File.Exists(jsonPath))
            return id;

        ctx.Status.Report($"loader.installing:{LoaderNames.Display(Type)}");
        var profile = await api.GetStringAsync(
            $"{MetaUrl}/{Uri.EscapeDataString(ctx.MinecraftVersion)}/{Uri.EscapeDataString(version)}/profile/json",
            ct: ctx.Token);
        using var profileJson = JsonDocument.Parse(profile);
        id = profileJson.RootElement.GetProperty("id").GetString()!;
        if (!LoaderManager.IsSafeVersion(id))
            throw new AxoException("errors.badResponse", new() { ["host"] = new Uri(MetaUrl).Host });
        jsonPath = ctx.Path.GetVersionJsonPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        await File.WriteAllTextAsync(jsonPath, profile, ctx.Token);
        return id;
    }

    public bool IsInstalled(MinecraftPath path, string mc, string? loaderVersion) =>
        loaderVersion != null && File.Exists(path.GetVersionJsonPath(ProfileId(mc, loaderVersion)));

    private string ProfileId(string mc, string loader) => $"{ProfilePrefix}-{loader}-{mc}";
}

public sealed class FabricLoader(ApiClient api) : MetaProfileLoader(api)
{
    public override LoaderType Type => LoaderType.Fabric;
    protected override string MetaUrl => "https://meta.fabricmc.net/v2/versions/loader";
    protected override string ProfilePrefix => "fabric-loader";
}

public sealed class QuiltLoader(ApiClient api) : MetaProfileLoader(api)
{
    public override LoaderType Type => LoaderType.Quilt;
    protected override string MetaUrl => "https://meta.quiltmc.org/v3/versions/loader";
    protected override string ProfilePrefix => "quilt-loader";

    /// <summary>Quilt markiert Betas nur über "-beta" im Namen; neue MC-Versionen haben oft nur Betas.</summary>
    protected override string? PickRecommended(string mc, List<(string Version, bool Stable)> versions) =>
        versions.FirstOrDefault(v => !v.Version.Contains('-')).Version ?? versions.FirstOrDefault().Version;
}

public sealed class ForgeLoader(ApiClient api) : ILoaderInstaller
{
    private const string Promotions = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string Metadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

    public LoaderType Type => LoaderType.Forge;

    public async Task<IReadOnlyList<LoaderVersionInfo>> ListVersionsAsync(string mc, CancellationToken ct)
    {
        var xml = XDocument.Parse(await api.GetStringAsync(Metadata, TimeSpan.FromHours(6), ct));
        var prefix = mc + "-";
        var versions = xml.Descendants("version").Select(v => v.Value)
            .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
            .Select(v => v[prefix.Length..].Split('-')[0])
            .Distinct()
            .ToList();
        string? recommended = null;
        try
        {
            using var promos = JsonDocument.Parse(await api.GetStringAsync(Promotions, TimeSpan.FromHours(6), ct));
            var p = promos.RootElement.GetProperty("promos");
            recommended = p.TryGetProperty(mc + "-recommended", out var r) ? r.GetString()
                : p.TryGetProperty(mc + "-latest", out var l) ? l.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
        }
        recommended ??= versions.FirstOrDefault();
        return versions.Select(v => new LoaderVersionInfo(v, true, v == recommended)).ToList();
    }

    public async Task<string> InstallAsync(LoaderInstallContext ctx)
    {
        ctx.Status.Report("loader.installing:Forge");
        var forge = new ForgeInstaller(ctx.Launcher);
        var options = new ForgeInstallOptions
        {
            FileProgress = ctx.Files,
            ByteProgress = ctx.Bytes,
            InstallerOutput = new Progress<string>(_ => { }),
            SkipIfAlreadyInstalled = true,
            CancellationToken = ctx.Token
        };
        return LoaderManager.IsSafeVersion(ctx.LoaderVersion)
            ? await forge.Install(ctx.MinecraftVersion, ctx.LoaderVersion!, options)
            : await forge.Install(ctx.MinecraftVersion, options);
    }

    public bool IsInstalled(MinecraftPath path, string mc, string? loaderVersion) =>
        loaderVersion != null && (File.Exists(path.GetVersionJsonPath($"{mc}-forge-{loaderVersion}"))
                                  || File.Exists(path.GetVersionJsonPath($"{mc}-forge{mc}-{loaderVersion}")));
}

public sealed class NeoForgeLoader(ApiClient api) : ILoaderInstaller
{
    private const string Metadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

    public LoaderType Type => LoaderType.NeoForge;

    /// <summary>MC 1.21.4 → NeoForge 21.4.x; MC 26.1 → NeoForge 26.1.x.</summary>
    internal static string? Prefix(string mc)
    {
        var parsed = VersionManager.Parse(mc);
        if (parsed == null)
            return null;
        var (major, minor, patch) = parsed.Value;
        return major == 1 ? $"{minor}.{patch}." : $"{major}.{minor}.";
    }

    public async Task<IReadOnlyList<LoaderVersionInfo>> ListVersionsAsync(string mc, CancellationToken ct)
    {
        var prefix = Prefix(mc);
        if (prefix == null)
            return [];
        var xml = XDocument.Parse(await api.GetStringAsync(Metadata, TimeSpan.FromHours(6), ct));
        var versions = xml.Descendants("version").Select(v => v.Value)
            .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
            .Reverse()
            .ToList();
        var recommended = versions.FirstOrDefault(v => !v.Contains("beta") && !v.Contains("alpha")) ?? versions.FirstOrDefault();
        return versions.Select(v => new LoaderVersionInfo(v, !v.Contains("beta") && !v.Contains("alpha"), v == recommended)).ToList();
    }

    public async Task<string> InstallAsync(LoaderInstallContext ctx)
    {
        ctx.Status.Report("loader.installing:NeoForge");
        var neoForge = new NeoForgeInstaller(ctx.Launcher);
        var options = new NeoForgeInstallOptions
        {
            FileProgress = ctx.Files,
            ByteProgress = ctx.Bytes,
            InstallerOutput = new Progress<string>(_ => { }),
            SkipIfAlreadyInstalled = true,
            CancellationToken = ctx.Token
        };
        return LoaderManager.IsSafeVersion(ctx.LoaderVersion)
            ? await neoForge.Install(ctx.MinecraftVersion, ctx.LoaderVersion!, options)
            : await neoForge.Install(ctx.MinecraftVersion, options);
    }

    public bool IsInstalled(MinecraftPath path, string mc, string? loaderVersion) =>
        loaderVersion != null && File.Exists(path.GetVersionJsonPath($"neoforge-{loaderVersion}"));
}
