using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Instances;
using AxoClient.Diagnostics.Contract;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;
using AxoClient.Settings.Contract;

namespace AxoClient.Launch
{
    /// <summary>
    /// Maßnahmen aus dem Absturz-Dialog. Dieselben Bausteine wie in der Start-Pipeline (Mod deaktivieren, neu laden, RAM setzen).
    /// </summary>
    public sealed class CrashActions : ICrashActions
    {
        private readonly IInstanceManager _instances;
        private readonly IContentManager _content;
        private readonly IMinecraftInstaller _installer;
        private readonly IUpdateScanner _updates;
        private readonly ISettingsService _settings;
        private readonly IStorageService _storage;
        private readonly IPathGuard _pathGuard;
        private readonly ISystemInfo _systemInfo;

        public CrashActions(
            IInstanceManager instances,
            IContentManager content,
            IMinecraftInstaller installer,
            IUpdateScanner updates,
            ISettingsService settings,
            IStorageService storage,
            IPathGuard pathGuard,
            ISystemInfo systemInfo)
        {
            _instances = instances;
            _content = content;
            _installer = installer;
            _updates = updates;
            _settings = settings;
            _storage = storage;
            _pathGuard = pathGuard;
            _systemInfo = systemInfo;
        }

        public async Task Run(string instanceId, string action, string? file, JobContext job)
        {
            var instance = await _instances.Get(instanceId, job.Token);
            switch (action)
            {
                case "increaseRam":
                    {
                        var (total, _) = _systemInfo.Memory();
                        var current = instance.MemoryMb ?? _settings.Current.Defaults.MemoryMb;
                        var target = (int)Math.Min(current + 2048, Math.Max(2048, total * 7 / 10 / 512 * 512));
                        await _instances.Update(instanceId, new InstanceUpdate(MemoryMb: target), job.Token);
                        break;
                    }
                case "recommendedRam":
                    {
                        var doc = await _content.Sync(instanceId, job.Token);
                        var (total, _) = _systemInfo.Memory();
                        await _instances.Update(instanceId,
                            new InstanceUpdate(MemoryMb: _systemInfo.RecommendedMemoryMb(doc.Mods.Count(m => m.Enabled), total)), job.Token);
                        break;
                    }
                case "javaAuto":
                    await _settings.Update(s => s.Defaults.Java = "auto");
                    break;
                case "repairInstance":
                case "repairGameFiles":
                    await repair(instanceId, job);
                    break;
                case "checkMods":
                    // Beim nächsten Start alle Kompatibilitätsprüfungen erzwingen
                    await _content.Change(instanceId, doc => doc.CheckedAt = null, job.Token);
                    break;
                case "disableMod":
                    await _content.SetEnabled(instanceId, ContentType.Mod, requireFile(file), false, job.Token);
                    break;
                case "updateMod":
                    {
                        var found = await _updates.Scan(instanceId, job.Token);
                        var match = found.Where(u => u.File == requireFile(file)).ToList();
                        if (match.Count == 0)
                        {
                            throw new AxoException("crash.noUpdate");
                        }

                        await _updates.Apply(instanceId, match, job);
                        break;
                    }
                case "keepNewest":
                    await _content.Change(instanceId, doc => doc.CheckedAt = null, job.Token);
                    break;
                case "disableShaders":
                    {
                        var doc = await _content.Sync(instanceId, job.Token);
                        foreach (var shader in doc.Shaderpacks.Where(s => s.Enabled))
                        {
                            await _content.SetEnabled(instanceId, ContentType.ShaderPack, shader.File, false, job.Token);
                        }
                        // Iris merkt sich den aktiven Shader selbst: dort ebenfalls ausschalten
                        var iris = Path.Combine(_instances.GameDir(instanceId), "config", "iris.properties");
                        if (File.Exists(iris))
                        {
                            var lines = (await File.ReadAllLinesAsync(iris, job.Token))
                                .Select(l => l.StartsWith("enableShaders=") ? "enableShaders=false" : l);
                            await File.WriteAllLinesAsync(iris, lines, job.Token);
                        }
                        break;
                    }
                case "resetConfig":
                    {
                        var configDir = Path.Combine(_instances.GameDir(instanceId), "config");
                        var path = _pathGuard.Combine(configDir, requireFile(file));
                        if (File.Exists(path))
                        {
                            File.Move(path, path + $".axoclient-backup-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
                        }

                        break;
                    }
                case "rollbackMod":
                    await _content.Rollback(instanceId, ContentType.Mod, requireFile(file), job.Token);
                    break;
                case "cleanCache":
                    {
                        // Nur, was der Launcher jederzeit neu holen kann: Caches und alte Logs. Welten und Backups bleiben.
                        _storage.CleanCache();
                        _storage.CleanLogs();
                        var root = Path.GetPathRoot(Path.GetFullPath(_instances.GameDir(instanceId)));
                        if (root != null && new DriveInfo(root).AvailableFreeSpace < 1L << 30)
                        {
                            throw new AxoException("crash.diskStillFull");
                        }

                        break;
                    }
                default:
                    throw new AxoException("errors.invalidInput");
            }
        }

        private async Task repair(string instanceId, JobContext job)
        {
            var instance = await _instances.Get(instanceId, job.Token);
            var path = _installer.PathFor(_instances.GameDir(instanceId));
            await _installer.Install(path, instance.Minecraft.Version, instance.Loader.Type, instance.Loader.Version, job);
            // Inhalte: beim nächsten Start alles gegen die Hashes prüfen
            await _content.Change(instanceId, doc =>
            {
                foreach (var entry in Enum.GetValues<ContentType>().SelectMany(doc.ListOf))
                {
                    entry.Modified = null;
                }

                doc.CheckedAt = null;
            }, job.Token);
        }

        private string requireFile(string? file) =>
            _pathGuard.IsSafeFileName(file) ? file! : throw new AxoException("errors.invalidFileName");
    }
}
