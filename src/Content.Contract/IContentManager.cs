using AxoClient.Common;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Instances;

namespace AxoClient.Content.Contract
{
    public interface IContentManager
    {
        string Folder(string instanceId, ContentType type);

        Task<IReadOnlyList<ContentItem>> List(string instanceId, ContentType type, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gleicht Ordner und content.json ab: unbekannte Dateien werden als <c>local</c> eingetragen, Einträge ohne
        /// Datei entfernt. Hashes werden nur neu berechnet, wenn sich Größe oder Änderungszeit geändert haben.
        /// </summary>
        Task<ContentDocument> Sync(string instanceId, CancellationToken cancellationToken = default);

        /// <summary>Unbekannte lokale Dateien bei Modrinth erkennen, damit sie Name, Icon und Updates bekommen.</summary>
        Task IdentifyLocal(string instanceId, CancellationToken cancellationToken = default);

        /// <summary>Installiert eine Version samt Pflicht-Abhängigkeiten. Gibt die installierten Dateinamen zurück.</summary>
        Task<IReadOnlyList<string>> Install(string instanceId, ContentType type, InstallRequest request,
        JobContext job, bool ignoreModLock = false);

        /// <summary>Lokale Datei hinzufügen (Drag &amp; Drop). Gilt als "Lokal · eigene Mod".</summary>
        Task<string> AddLocalFile(string instanceId, ContentType type, string sourcePath, CancellationToken cancellationToken = default);

        Task SetEnabled(string instanceId, ContentType type, string file, bool enabled, CancellationToken cancellationToken = default);

        Task Remove(string instanceId, ContentType type, IReadOnlyCollection<string> files, CancellationToken cancellationToken = default);

        /// <summary>Mod-Schutz umstellen. <paramref name="adoptCurrent"/>: alle jetzigen Mods zählen ab sofort zum Pack.</summary>
        Task<ModLock> SetModLock(string instanceId, ModLockLevel level, bool adoptCurrent, CancellationToken cancellationToken = default);

        /// <summary>Verfügbare Versionen für "Version ändern".</summary>
        Task<IReadOnlyList<ContentVersion>> VersionsFor(string instanceId, ContentType type, string file,
        bool allMinecraftVersions, CancellationToken cancellationToken = default);

        /// <summary>
        /// Nach einem Wechsel von Minecraft-Version oder Loader: Mods von Modrinth/CurseForge auf eine passende Version
        /// umstellen, Mods ohne passende Version deaktivieren (nicht löschen). Lokale Mods werden nur deaktiviert, wenn ihre
        /// Metadaten klar sagen, dass sie nicht passen. Gesperrte Pack-Mods und bereits deaktivierte Mods bleiben, wie sie sind.
        /// </summary>
        Task<ModAdaptResult> AdaptMods(string instanceId, JobContext job);

        Task Change(string instanceId, Action<ContentDocument> change, CancellationToken cancellationToken = default);

        ProjectKind ToKind(ContentType type);

        bool SupportsMods(Instance instance);

        /// <summary>Letztes Update bzw. letzten Versionswechsel eines Inhalts zurücknehmen.</summary>
        Task Rollback(string instanceId, ContentType type, string file, CancellationToken cancellationToken = default);

        bool IsContentFile(ContentType type, string name);

        /// <summary>Dateiname ohne <c>.disabled</c>.</summary>
        string BaseName(string name);
    }
}
