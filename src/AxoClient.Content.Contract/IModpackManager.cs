using AxoClient.Common;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Instances;

namespace AxoClient.Content.Contract
{
    public interface IModpackManager
    {
        /// <summary>Modpack von Modrinth als neue Instanz installieren.</summary>
        Task<Instance> InstallFromSource(string source, string projectId, string? versionId, JobContext job);

        /// <summary>Eine <c>.mrpack</c>-Datei als neue Instanz einlesen.</summary>
        Task<Instance> ImportMrpack(string packPath, JobContext job, string? nameOverride = null);

        /// <summary>Instanz auf eine neue Modpack-Version heben. Eigene Mods und Welten bleiben.</summary>
        Task Update(string instanceId, string versionId, JobContext job);

        /// <summary>
        /// Instanz als <c>.mrpack</c> teilen: Inhalte von Modrinth als Verweis, alles andere (außer der AxoClient-Mod)
        /// als Overrides, dazu <c>config/</c> und <c>options.txt</c>. Keine Welten.
        /// </summary>
        Task ExportMrpack(string instanceId, string targetPath, JobContext job);

        /// <summary>Für die Modpack-Seite: verfügbare Versionen eines Packs.</summary>
        Task<IReadOnlyList<ContentVersion>> Versions(string source, string projectId, CancellationToken ct);
    }
}
