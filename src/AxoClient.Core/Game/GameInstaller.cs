using AxoClient.Core.Common;
using CmlLib.Core;
using CmlLib.Core.Installers;

namespace AxoClient.Core.Game;

/// <summary>Installiert Minecraft-Version und Loader über CmlLib in den gemeinsamen <c>game/</c>-Ordner.</summary>
public sealed class GameInstaller(AppPaths paths, LoaderManager loaders, HttpClient http)
{
    /// <summary>Gemeinsame Bibliotheken, Versionen und Assets; Spielordner pro Instanz.</summary>
    public MinecraftPath PathFor(string gameDir) => new(gameDir)
    {
        Library = paths.Libraries,
        Versions = paths.Versions,
        Resource = Path.Combine(gameDir, "resources"),
        Assets = paths.Assets,
        Runtime = paths.Java
    };

    public MinecraftLauncher CreateLauncher(MinecraftPath path) =>
        new(MinecraftLauncherParameters.CreateDefault(path, http));

    /// <summary>Versions-ID, falls Version und Loader schon installiert sind (für Offline-Starts).</summary>
    public string? InstalledVersionName(string mc, LoaderType loader, string? loaderVersion)
    {
        string? candidate = loader switch
        {
            LoaderType.Vanilla => mc,
            LoaderType.Fabric => loaderVersion != null ? $"fabric-loader-{loaderVersion}-{mc}" : null,
            LoaderType.Quilt => loaderVersion != null ? $"quilt-loader-{loaderVersion}-{mc}" : null,
            LoaderType.Forge => loaderVersion != null ? $"{mc}-forge-{loaderVersion}" : null,
            LoaderType.NeoForge => loaderVersion != null ? $"neoforge-{loaderVersion}" : null,
            _ => null
        };
        if (candidate != null && File.Exists(Path.Combine(paths.Versions, candidate, candidate + ".json")))
            return candidate;
        if (loader == LoaderType.Forge && loaderVersion != null)
        {
            var legacy = $"{mc}-forge{mc}-{loaderVersion}";
            if (File.Exists(Path.Combine(paths.Versions, legacy, legacy + ".json")))
                return legacy;
        }
        return null;
    }

    /// <summary>Sind Client-Jar und Versions-JSON vorhanden? (Grobe Offline-Prüfung.)</summary>
    public bool IsVersionPresent(string versionName)
    {
        var json = Path.Combine(paths.Versions, versionName, versionName + ".json");
        if (!File.Exists(json))
            return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
            var root = doc.RootElement;
            var jarId = root.TryGetProperty("inheritsFrom", out var parent) ? parent.GetString()! : versionName;
            if (root.TryGetProperty("jar", out var jar) && jar.GetString() is { } j)
                jarId = j;
            return File.Exists(Path.Combine(paths.Versions, jarId, jarId + ".jar"));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            return false;
        }
    }

    /// <summary>Loader installieren (falls nötig) und alle Spieldateien laden bzw. reparieren.</summary>
    public async Task<string> InstallAsync(MinecraftPath path, string mc, LoaderType loader, string? loaderVersion, JobContext job)
    {
        var launcher = CreateLauncher(path);
        var status = new Progress<string>(s =>
        {
            if (s.StartsWith("loader.installing:"))
                job.Status("pipeline.installingLoader", ("loader", s["loader.installing:".Length..]));
        });
        var files = new Progress<InstallerProgressChangedEventArgs>(e =>
        {
            if (e.TotalTasks > 0)
                job.Report(0.1 + 0.85 * e.ProgressedTasks / e.TotalTasks, TextRef.Of("pipeline.downloadingFiles",
                    ("done", e.ProgressedTasks), ("total", e.TotalTasks)));
        });
        var bytes = new Progress<ByteProgress>(_ => { });

        var versionName = mc;
        if (loader != LoaderType.Vanilla)
        {
            var installer = loaders.For(loader) ?? throw new AxoException("game.loaderUnsupported",
                new() { ["loader"] = LoaderNames.Display(loader), ["version"] = mc });
            versionName = InstalledVersionName(mc, loader, loaderVersion)
                          ?? await installer.InstallAsync(new LoaderInstallContext(launcher, path, mc, loaderVersion, status, files,
                              bytes, job.Token));
        }

        job.Status("pipeline.verifyingFiles");
        await launcher.InstallAsync(versionName, files, bytes, job.Token);
        return versionName;
    }
}
