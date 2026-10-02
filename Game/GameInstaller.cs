using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;

namespace AxoClient.Game;

public record QuickPlay(string? World = null, string? Server = null);

public record LaunchProgress(
    IProgress<string> Status,
    IProgress<InstallerProgressChangedEventArgs> Files,
    IProgress<ByteProgress> Bytes);

public class GameInstaller(HttpClient http)
{
    private const string FabricMeta = "https://meta.fabricmc.net/v2/versions/loader";
    private const string QuiltMeta = "https://meta.quiltmc.org/v3/versions/loader";

    public static bool IsSafeLoaderVersion(string? version) =>
        version is { Length: > 0 and <= 40 } && Regex.IsMatch(version, @"^[A-Za-z0-9._+\-]+$");

    private static MinecraftPath PathFor(Installation inst) => new(inst.GameDir)
    {
        Library = AppPaths.Libraries,
        Versions = AppPaths.Versions,
        Resource = AppPaths.Resources,
        Assets = AppPaths.Assets,
        Runtime = AppPaths.Runtime
    };

    public async Task<Process> PrepareAsync(Installation inst, MSession session, LauncherSettings settings,
        LaunchProgress progress, QuickPlay? quickPlay)
    {
        var path = PathFor(inst);
        Directory.CreateDirectory(inst.GameDir);
        if (inst.CanUseMods)
            Directory.CreateDirectory(inst.ModsDir);

        var launcher = new MinecraftLauncher(path);
        var versionName = await InstallLoaderAsync(launcher, path, inst, progress);

        progress.Status.Report("Lade Spieldateien...");
        await launcher.InstallAsync(versionName, progress.Files, progress.Bytes);

        var option = new MLaunchOption
        {
            Session = session,
            MaximumRamMb = inst.MaxRamMb ?? settings.MaxRamMb,
            FullScreen = settings.FullScreen
        };
        if (JavaRuntimes.ResolveOverride(inst, settings) is { } javaPath)
            option.JavaPath = javaPath;
        if (settings.GameWidth > 0 && settings.GameHeight > 0)
        {
            option.ScreenWidth = settings.GameWidth;
            option.ScreenHeight = settings.GameHeight;
        }
        if (quickPlay?.Server is { } server)
        {
            ApplyPackProfile(inst, server, progress.Status);
            var (host, port) = ServerPing.ParseAddress(server);
            option.ServerIp = host;
            option.ServerPort = port;
        }
        if (quickPlay?.World is { } world)
            option.QuickPlaySingleplayer = world;

        var jvmArguments = new List<MArgument>();
        if (JvmPresets.Resolve(settings, inst) is { Length: > 0 } extraJvm)
            jvmArguments.Add(MArgument.FromCommandLine(extraJvm));
        BadgeMod.Sync(inst, settings);
        if (BadgeMod.IsActiveFor(inst, settings))
            jvmArguments.AddRange(BadgeMod.JvmArguments().Select(argument => new MArgument(argument)));
        if (jvmArguments.Count > 0)
            option.ExtraJvmArguments = jvmArguments;

        return await launcher.BuildProcessAsync(versionName, option);
    }

    private static void ApplyPackProfile(Installation inst, string server, IProgress<string> status)
    {
        var packs = new PackProfileStore(inst);
        if (packs.ForServer(server) is not { } profile)
            return;
        status.Report($"Setze Paket-Profil \"{profile.Name}\"...");
        packs.Apply(profile);
    }

    private async Task<string> InstallLoaderAsync(MinecraftLauncher launcher, MinecraftPath path, Installation inst,
        LaunchProgress progress)
    {
        switch (inst.Loader)
        {
            case LoaderType.Fabric:
                progress.Status.Report("Installiere Fabric...");
                return await InstallFabricAsync(path, inst.MinecraftVersion, inst.LoaderVersion);
            case LoaderType.Quilt:
                progress.Status.Report("Installiere Quilt...");
                return await InstallQuiltAsync(path, inst.MinecraftVersion, inst.LoaderVersion);
            case LoaderType.Forge:
                progress.Status.Report("Installiere Forge (kann beim ersten Mal etwas dauern)...");
                return await InstallForgeAsync(launcher, inst, new ForgeInstallOptions
                {
                    FileProgress = progress.Files,
                    ByteProgress = progress.Bytes,
                    InstallerOutput = progress.Status,
                    SkipIfAlreadyInstalled = true
                }, progress.Status);
            case LoaderType.NeoForge:
                progress.Status.Report("Installiere NeoForge (kann beim ersten Mal etwas dauern)...");
                return await InstallNeoForgeAsync(launcher, inst, new NeoForgeInstallOptions
                {
                    FileProgress = progress.Files,
                    ByteProgress = progress.Bytes,
                    InstallerOutput = progress.Status,
                    SkipIfAlreadyInstalled = true
                }, progress.Status);
            default:
                return inst.MinecraftVersion;
        }
    }

    private static async Task<string> InstallForgeAsync(MinecraftLauncher launcher, Installation inst,
        ForgeInstallOptions options, IProgress<string> status)
    {
        var forge = new ForgeInstaller(launcher);
        if (IsSafeLoaderVersion(inst.LoaderVersion))
        {
            try
            {
                return await forge.Install(inst.MinecraftVersion, inst.LoaderVersion!, options);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorReport.Log($"Forge {inst.LoaderVersion} installieren", ex);
                status.Report($"Forge {inst.LoaderVersion} nicht verfügbar ({ErrorReport.Short(ex)}), " +
                              "nehme die empfohlene Version...");
            }
        }
        return await forge.Install(inst.MinecraftVersion, options);
    }

    private static async Task<string> InstallNeoForgeAsync(MinecraftLauncher launcher, Installation inst,
        NeoForgeInstallOptions options, IProgress<string> status)
    {
        var neoForge = new NeoForgeInstaller(launcher);
        if (IsSafeLoaderVersion(inst.LoaderVersion))
        {
            try
            {
                return await neoForge.Install(inst.MinecraftVersion, inst.LoaderVersion!, options);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorReport.Log($"NeoForge {inst.LoaderVersion} installieren", ex);
                status.Report($"NeoForge {inst.LoaderVersion} nicht verfügbar ({ErrorReport.Short(ex)}), " +
                              "nehme die neueste Version...");
            }
        }
        return await neoForge.Install(inst.MinecraftVersion, options);
    }

    private Task<string> InstallFabricAsync(MinecraftPath path, string mcVersion, string? pinnedLoader) =>
        InstallProfileAsync(path, FabricMeta, "Fabric", "fabric-loader", mcVersion, pinnedLoader,
            loaders => loaders.FirstOrDefault(l => l.TryGetProperty("stable", out var stable) && stable.GetBoolean(), loaders[0]));

    private Task<string> InstallQuiltAsync(MinecraftPath path, string mcVersion, string? pinnedLoader) =>
        InstallProfileAsync(path, QuiltMeta, "Quilt", "quilt-loader", mcVersion, pinnedLoader, loaders =>
        {
            var sorted = loaders.OrderByDescending(l => LoaderVersionKey(l.GetProperty("version").GetString()!)).ToList();
            var needsNewest = int.TryParse(mcVersion.Split('.')[0], out var major) && major >= 26;
            return needsNewest
                ? sorted[0]
                : sorted.FirstOrDefault(l => !l.GetProperty("version").GetString()!.Contains('-'), sorted[0]);
        });

    private static (int, int, int, int, int) LoaderVersionKey(string version)
    {
        var dash = version.IndexOf('-');
        var release = (dash < 0 ? version : version[..dash]).Split('.')
            .Select(p => int.TryParse(p, out var n) ? n : 0).Concat([0, 0, 0]).ToArray();
        var pre = dash < 0 ? int.MaxValue : int.TryParse(version[(version.LastIndexOf('.') + 1)..], out var b) ? b : 0;
        return (release[0], release[1], release[2], dash < 0 ? 1 : 0, pre);
    }

    private async Task<string> InstallProfileAsync(MinecraftPath path, string meta, string name, string prefix,
        string mcVersion, string? pinnedLoader, Func<List<JsonElement>, JsonElement> pick)
    {
        try
        {
            if (IsSafeLoaderVersion(pinnedLoader))
            {
                try
                {
                    return await WriteProfileAsync(path, meta, mcVersion, pinnedLoader!);
                }
                catch (HttpRequestException)
                {
                }
            }

            using var loaders = JsonDocument.Parse(await http.GetStringAsync($"{meta}/{mcVersion}"));
            var entries = loaders.RootElement.EnumerateArray().Select(e => e.GetProperty("loader")).ToList();
            if (entries.Count == 0)
                throw new InvalidOperationException($"{name} unterstützt Minecraft {mcVersion} nicht.");
            return await WriteProfileAsync(path, meta, mcVersion, pick(entries).GetProperty("version").GetString()!);
        }
        catch (HttpRequestException)
        {
            var installed = Directory.Exists(path.Versions)
                ? new DirectoryInfo(path.Versions).GetDirectories($"{prefix}-*-{mcVersion}")
                    .OrderByDescending(d => d.Name == $"{prefix}-{pinnedLoader}-{mcVersion}")
                    .ThenByDescending(d => d.LastWriteTime).FirstOrDefault()
                : null;
            return installed?.Name ?? throw new InvalidOperationException(
                $"{name} konnte nicht geladen werden und ist für diese Version noch nicht installiert.");
        }
    }

    private async Task<string> WriteProfileAsync(MinecraftPath path, string meta, string mcVersion, string loaderVersion)
    {
        var profile = await http.GetStringAsync($"{meta}/{mcVersion}/{loaderVersion}/profile/json");
        using var profileJson = JsonDocument.Parse(profile);
        var id = profileJson.RootElement.GetProperty("id").GetString()!;

        var jsonPath = path.GetVersionJsonPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        await File.WriteAllTextAsync(jsonPath, profile);
        return id;
    }
}
