using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;
using CmlLib.Core;
using CmlLib.Core.Installers;

namespace AxoClient.Game
{
    /// <summary>Installiert Minecraft-Version und Loader über CmlLib in den gemeinsamen <c>game/</c>-Ordner.</summary>
    public sealed class MinecraftInstaller : IMinecraftInstaller
    {
        private readonly AppPaths _paths;
        private readonly ILoaderManager _loaders;
        private readonly HttpClient _http;
        private readonly ILoaderNames _loaderNames;
        private readonly IGameVersionHelper _gameVersionHelper;

        public MinecraftInstaller(
            AppPaths paths,
            ILoaderManager loaders,
            HttpClient http,
            ILoaderNames loaderNames,
            IGameVersionHelper gameVersionHelper)
        {
            _paths = paths;
            _loaders = loaders;
            _http = http;
            _loaderNames = loaderNames;
            _gameVersionHelper = gameVersionHelper;
        }

        /// <summary>Gemeinsame Bibliotheken, Versionen und Assets; Spielordner pro Instanz.</summary>
        public MinecraftPath PathFor(string gameDirectory) => new(gameDirectory)
        {
            Library = _paths.Libraries,
            Versions = _paths.Versions,
            Resource = Path.Combine(gameDirectory, "resources"),
            Assets = _paths.Assets,
            Runtime = _paths.Java
        };

        public MinecraftLauncher CreateLauncher(MinecraftPath path) =>
            new(MinecraftLauncherParameters.CreateDefault(path, _http));

        /// <summary>Versions-ID, falls Version und Loader schon installiert sind (für Offline-Starts).</summary>
        public string? InstalledVersionName(string minecraftVersion, LoaderType loader, string? loaderVersion)
        {
            string? candidate = loader switch
            {
                LoaderType.Vanilla => minecraftVersion,
                LoaderType.Fabric => loaderVersion != null ? $"fabric-loader-{loaderVersion}-{minecraftVersion}" : null,
                LoaderType.Quilt => loaderVersion != null ? $"quilt-loader-{loaderVersion}-{minecraftVersion}" : null,
                LoaderType.Forge => loaderVersion != null ? $"{minecraftVersion}-forge-{loaderVersion}" : null,
                LoaderType.NeoForge => loaderVersion != null ? _gameVersionHelper.NeoForgeVersionId(minecraftVersion, loaderVersion) : null,
                _ => null
            };
            if (candidate != null && File.Exists(Path.Combine(_paths.Versions, candidate, candidate + ".json")))
            {
                return candidate;
            }

            if (loader == LoaderType.Forge && loaderVersion != null)
            {
                var legacy = $"{minecraftVersion}-forge{minecraftVersion}-{loaderVersion}";
                if (File.Exists(Path.Combine(_paths.Versions, legacy, legacy + ".json")))
                {
                    return legacy;
                }
            }
            return null;
        }

        /// <summary>Sind Client-Jar und Versions-JSON vorhanden? (Grobe Offline-Prüfung.)</summary>
        public bool IsVersionPresent(string versionName)
        {
            var json = Path.Combine(_paths.Versions, versionName, versionName + ".json");
            if (!File.Exists(json))
            {
                return false;
            }

            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
                var root = document.RootElement;
                var jarId = root.TryGetProperty("inheritsFrom", out var parent) ? parent.GetString()! : versionName;
                if (root.TryGetProperty("jar", out var jar) && jar.GetString() is { } jarName)
                {
                    jarId = jarName;
                }

                return File.Exists(Path.Combine(_paths.Versions, jarId, jarId + ".jar"));
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException)
            {
                return false;
            }
        }

        /// <summary>Loader installieren (falls nötig) und alle Spieldateien laden bzw. reparieren.</summary>
        public async Task<string> Install(MinecraftPath path, string minecraftVersion, LoaderType loader, string? loaderVersion, JobContext job)
        {
            var launcher = CreateLauncher(path);
            var status = new Progress<string>(statusText =>
            {
                if (statusText.StartsWith("loader.installing:"))
                {
                    job.Status("pipeline.installingLoader", ("loader", statusText["loader.installing:".Length..]));
                }
            });
            var files = new Progress<InstallerProgressChangedEventArgs>(installerProgressChangedEventArgs =>
            {
                if (installerProgressChangedEventArgs.TotalTasks > 0)
                {
                    job.Report(0.1 + 0.85 * installerProgressChangedEventArgs.ProgressedTasks / installerProgressChangedEventArgs.TotalTasks, new TextRef("pipeline.downloadingFiles",
                        ("done", installerProgressChangedEventArgs.ProgressedTasks), ("total", installerProgressChangedEventArgs.TotalTasks)));
                }
            });
            var bytes = new Progress<ByteProgress>(_ => { });

            var versionName = minecraftVersion;
            if (loader != LoaderType.Vanilla)
            {
                var installer = _loaders.For(loader) ?? throw new AxoException("game.loaderUnsupported",
                    new() { ["loader"] = _loaderNames.Display(loader), ["version"] = minecraftVersion });
                versionName = InstalledVersionName(minecraftVersion, loader, loaderVersion)
                              ?? await installer.Install(new LoaderInstallContext(launcher, path, minecraftVersion, loaderVersion, status, files,
                                  bytes, job.Token));
            }

            job.Status("pipeline.verifyingFiles");
            await launcher.InstallAsync(versionName, files, bytes, job.Token);
            return versionName;
        }
    }
}
