using AxoClient.Common;
using AxoClient.DataModel.Instances;

namespace AxoClient.Instances.Contract
{
    public interface IWorldManager
    {
        Task<IReadOnlyList<WorldInfo>> List(string instanceId, CancellationToken ct = default);

        int Count(string savesDir);

        string? IconPath(string instanceId, string folder);

        string WorldDir(string instanceId, string folder);

        /// <summary>Welt als ZIP in <c>backups/</c> sichern.</summary>
        Task<BackupInfo> Backup(string instanceId, string folder, JobContext job);

        /// <summary>
        /// Alle Welten sichern, z. B. vor einem Wechsel von Version oder Loader: Minecraft wandelt Welten beim ersten Öffnen
        /// um, und fehlende Mods entfernen ihre Blöcke. Bricht ab, bevor das Laufwerk vollläuft.
        /// </summary>
        Task<int> BackupAll(string instanceId, JobContext job);

        IReadOnlyList<BackupInfo> ListBackups(string instanceId);

        /// <summary>Backup wiederherstellen. Gibt es die Welt schon, landet sie unter neuem Namen daneben.</summary>
        Task<string> RestoreBackup(string instanceId, string backupFile, JobContext job);

        /// <summary>
        /// Welt aus einer ZIP übernehmen (Download oder Export). Die Welt ist der Ordner mit der obersten
        /// <c>level.dat</c>; gibt es den Namen schon, landet sie unter neuem Namen daneben.
        /// </summary>
        Task<string> Import(string instanceId, string zipPath, JobContext job);

        Task Delete(string instanceId, string folder, CancellationToken ct = default);
    }
}
