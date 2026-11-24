using AxoClient.DataModel.Diagnostics;

namespace AxoClient.Diagnostics.Contract
{
    public interface IStorageService
    {
        Task<StorageOverview> Scan(CancellationToken ct);

        /// <summary>Welt-Backups älter als <paramref name="keepDays"/> Tage löschen; das jeweils neueste pro Welt bleibt.</summary>
        long CleanBackups(int keepDays = 30);

        /// <summary>Minecraft-Versionen entfernen, die keine Instanz mehr nutzt (Libraries und Assets bleiben).</summary>
        Task<long> CleanVersions(CancellationToken ct);

        /// <summary>Java-Runtimes entfernen, die keine Version mehr braucht (beim nächsten Start lädt CmlLib bei Bedarf neu).</summary>
        long CleanJava();

        long CleanCache();

        long CleanLogs();
    }
}
