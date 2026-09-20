using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;

namespace McLauncher;

/// <summary>Direkt in eine Welt (Ordnername) oder auf einen Server (Adresse) starten.</summary>
public record QuickPlay(string? World = null, string? Server = null);

/// <summary>Installiert eine Installation (Vanilla, Fabric oder Forge) und baut den Spielprozess.</summary>
public class GameInstaller(string sharedDir, HttpClient http)
{
    /// <summary>
    /// Bibliotheken, Versionen, Assets und Java werden von allen Installationen geteilt,
    /// nur der Spielordner (Welten, Mods, Optionen) ist pro Installation eigen.
    /// </summary>
    public MinecraftPath CreatePath(Installation inst) => new(inst.GameDir)
    {
        Library = Path.Combine(sharedDir, "libraries"),
        Versions = Path.Combine(sharedDir, "versions"),
        Resource = Path.Combine(sharedDir, "resources"),
        Assets = Path.Combine(sharedDir, "assets"),
        Runtime = Path.Combine(sharedDir, "runtime")
    };

    public async Task<Process> PrepareAsync(
        Installation inst,
        MSession session,
        LauncherSettings settings,
        IProgress<string> status,
        IProgress<InstallerProgressChangedEventArgs> fileProgress,
        IProgress<ByteProgress> byteProgress,
        QuickPlay? quickPlay = null)
    {
        var path = CreatePath(inst);
        Directory.CreateDirectory(inst.GameDir);
        if (inst.Loader != LoaderType.Vanilla)
            Directory.CreateDirectory(Path.Combine(inst.GameDir, "mods"));

        var launcher = new MinecraftLauncher(path);

        string versionName;
        switch (inst.Loader)
        {
            case LoaderType.Fabric:
                status.Report("Installiere Fabric...");
                versionName = await InstallFabricAsync(path, inst.MinecraftVersion, inst.LoaderVersion);
                break;

            case LoaderType.Forge:
                status.Report("Installiere Forge (kann beim ersten Mal etwas dauern)...");
                versionName = await InstallForgeAsync(launcher, inst, new ForgeInstallOptions
                {
                    FileProgress = fileProgress,
                    ByteProgress = byteProgress,
                    InstallerOutput = status,
                    SkipIfAlreadyInstalled = true
                }, status);
                break;

            default:
                versionName = inst.MinecraftVersion;
                break;
        }

        status.Report("Lade Spieldateien...");
        await launcher.InstallAsync(versionName, fileProgress, byteProgress);

        var option = new MLaunchOption
        {
            Session = session,
            MaximumRamMb = inst.MaxRamMb ?? settings.MaxRamMb,
            FullScreen = settings.FullScreen
        };
        if (settings.GameWidth > 0 && settings.GameHeight > 0)
        {
            option.ScreenWidth = settings.GameWidth;
            option.ScreenHeight = settings.GameHeight;
        }
        if (quickPlay?.Server is { } server)
        {
            // CmlLib wählt je nach Version --server/--port oder das neuere Quick Play
            var (host, port) = ServerPing.ParseAddress(server);
            option.ServerIp = host;
            option.ServerPort = port;
        }
        if (quickPlay?.World is { } world)
            option.QuickPlaySingleplayer = world; // wird erst ab Minecraft 1.20 unterstützt
        var jvmArguments = new List<MArgument>();
        if (!string.IsNullOrWhiteSpace(settings.JvmArguments))
            jvmArguments.Add(MArgument.FromCommandLine(settings.JvmArguments));

        // Axolotl-Symbol in der Tabliste: Mod einrichten/entfernen und Dienst-Adresse übergeben (Anmeldung: FriendsService)
        Badge.SyncMod(inst, settings);
        if (Badge.IsActiveFor(inst, settings))
        {
            jvmArguments.Add(new MArgument(Badge.JvmArgument(settings)));
            jvmArguments.Add(new MArgument($"-Daxoclient.capes={settings.CapesUrl}"));
        }
        if (jvmArguments.Count > 0)
            option.ExtraJvmArguments = jvmArguments;

        return await launcher.BuildProcessAsync(versionName, option);
    }

    /// <summary>Loader-Versionen kommen aus Modpack-Dateien und landen in Adressen und Ordnernamen: nur harmlose Zeichen.</summary>
    public static bool IsSafeLoaderVersion(string? version) =>
        version is { Length: > 0 and <= 40 } && System.Text.RegularExpressions.Regex.IsMatch(version, @"^[A-Za-z0-9._+\-]+$");

    /// <summary>
    /// Forge in der festen Version der Instanz (z.B. aus einem Modpack). Gibt es sie nicht mehr, wird die
    /// empfohlene Version für die Minecraft-Version genommen, statt den Start ganz scheitern zu lassen.
    /// </summary>
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
                status.Report($"Forge {inst.LoaderVersion} nicht verfügbar, nehme die empfohlene Version...");
            }
        }
        return await forge.Install(inst.MinecraftVersion, options);
    }

    /// <summary>
    /// Holt das Versionsprofil des Fabric-Loaders (die feste Version der Instanz, sonst der neueste stabile)
    /// und legt es als eigene Version ab. CmlLib erbt den Rest (Spiel, Assets) automatisch über "inheritsFrom".
    /// </summary>
    private async Task<string> InstallFabricAsync(MinecraftPath path, string mcVersion, string? pinnedLoader = null)
    {
        try
        {
            if (IsSafeLoaderVersion(pinnedLoader))
            {
                try
                {
                    return await WriteFabricProfileAsync(path, mcVersion, pinnedLoader!);
                }
                catch (HttpRequestException)
                {
                    // Diese Loader-Version gibt es (nicht mehr): stattdessen die neueste nehmen
                }
            }

            using var loaders = JsonDocument.Parse(
                await http.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}"));
            var entries = loaders.RootElement.EnumerateArray().Select(e => e.GetProperty("loader")).ToList();
            if (entries.Count == 0)
                throw new InvalidOperationException($"Fabric unterstützt Minecraft {mcVersion} nicht.");
            var loader = entries.FirstOrDefault(l => l.GetProperty("stable").GetBoolean(), entries[0]);
            return await WriteFabricProfileAsync(path, mcVersion, loader.GetProperty("version").GetString()!);
        }
        catch (HttpRequestException)
        {
            // Offline: bereits installierten Fabric-Loader für diese Version verwenden
            var installed = Directory.Exists(path.Versions)
                ? new DirectoryInfo(path.Versions).GetDirectories($"fabric-loader-*-{mcVersion}")
                    .OrderByDescending(d => d.Name == $"fabric-loader-{pinnedLoader}-{mcVersion}") // die feste Version zuerst
                    .ThenByDescending(d => d.LastWriteTime).FirstOrDefault()
                : null;
            return installed?.Name ?? throw new InvalidOperationException(
                "Fabric konnte nicht geladen werden und ist für diese Version noch nicht installiert.");
        }
    }

    private async Task<string> WriteFabricProfileAsync(MinecraftPath path, string mcVersion, string loaderVersion)
    {
        var profile = await http.GetStringAsync(
            $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json");
        using var profileJson = JsonDocument.Parse(profile);
        var id = profileJson.RootElement.GetProperty("id").GetString()!;

        var jsonPath = path.GetVersionJsonPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        await File.WriteAllTextAsync(jsonPath, profile);
        return id;
    }
}
