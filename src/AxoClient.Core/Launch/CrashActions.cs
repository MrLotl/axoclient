using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Launch;

/// <summary>
/// Maßnahmen aus dem Absturz-Dialog. Dieselben Bausteine wie in der Start-Pipeline (Mod deaktivieren, neu laden, RAM setzen).
/// </summary>
public sealed class CrashActions(
    InstanceManager instances,
    ContentManager content,
    GameInstaller installer,
    UpdateScanner updates,
    SettingsService settings)
{
    public async Task RunAsync(string instanceId, string action, string? file, JobContext job)
    {
        var instance = await instances.GetAsync(instanceId, job.Token);
        switch (action)
        {
            case "increaseRam":
            {
                var (total, _) = SystemInfo.Memory();
                var current = instance.MemoryMb ?? settings.Current.Defaults.MemoryMb;
                var target = (int)Math.Min(current + 2048, Math.Max(2048, total * 7 / 10 / 512 * 512));
                await instances.UpdateAsync(instanceId, new InstanceUpdate(MemoryMb: target), job.Token);
                break;
            }
            case "recommendedRam":
            {
                var doc = await content.SyncAsync(instanceId, job.Token);
                var (total, _) = SystemInfo.Memory();
                await instances.UpdateAsync(instanceId,
                    new InstanceUpdate(MemoryMb: SystemInfo.RecommendedMemoryMb(doc.Mods.Count(m => m.Enabled), total)), job.Token);
                break;
            }
            case "javaAuto":
                await settings.UpdateAsync(s => s.Defaults.Java = "auto");
                break;
            case "repairInstance":
            case "repairGameFiles":
                await RepairAsync(instanceId, job);
                break;
            case "checkMods":
                // Beim nächsten Start alle Kompatibilitätsprüfungen erzwingen
                await content.ChangeAsync(instanceId, doc => doc.CheckedAt = null, job.Token);
                break;
            case "disableMod":
                await content.SetEnabledAsync(instanceId, ContentType.Mod, RequireFile(file), false, job.Token);
                break;
            case "updateMod":
            {
                var found = await updates.ScanAsync(instanceId, job.Token);
                var match = found.Where(u => u.File == RequireFile(file)).ToList();
                if (match.Count == 0)
                    throw new AxoException("crash.noUpdate");
                await updates.ApplyAsync(instanceId, match, job);
                break;
            }
            case "keepNewest":
                await content.ChangeAsync(instanceId, doc => doc.CheckedAt = null, job.Token);
                break;
            case "disableShaders":
            {
                var doc = await content.SyncAsync(instanceId, job.Token);
                foreach (var shader in doc.Shaderpacks.Where(s => s.Enabled))
                    await content.SetEnabledAsync(instanceId, ContentType.ShaderPack, shader.File, false, job.Token);
                // Iris merkt sich den aktiven Shader selbst: dort ebenfalls ausschalten
                var iris = Path.Combine(instances.GameDir(instanceId), "config", "iris.properties");
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
                var configDir = Path.Combine(instances.GameDir(instanceId), "config");
                var path = PathGuard.Combine(configDir, RequireFile(file));
                if (File.Exists(path))
                    File.Move(path, path + $".axoclient-backup-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
                break;
            }
            default:
                throw new AxoException("errors.invalidInput");
        }
    }

    private async Task RepairAsync(string instanceId, JobContext job)
    {
        var instance = await instances.GetAsync(instanceId, job.Token);
        var path = installer.PathFor(instances.GameDir(instanceId));
        await installer.InstallAsync(path, instance.Minecraft.Version, instance.Loader.Type, instance.Loader.Version, job);
        // Inhalte: beim nächsten Start alles gegen die Hashes prüfen
        await content.ChangeAsync(instanceId, doc =>
        {
            foreach (var entry in Enum.GetValues<ContentType>().SelectMany(doc.ListOf))
                entry.Modified = null;
            doc.CheckedAt = null;
        }, job.Token);
    }

    private static string RequireFile(string? file) =>
        PathGuard.IsSafeFileName(file) ? file! : throw new AxoException("errors.invalidFileName");
}
