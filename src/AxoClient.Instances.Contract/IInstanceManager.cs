using AxoClient.Common;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Instances;

namespace AxoClient.Instances.Contract
{
    public interface IInstanceManager
    {
        JsonStore<Instance> StoreOf(string id);

        JsonStore<ContentDocument> ContentOf(string id);

        string GameDir(string id);

        string ProcessKey(string id);

        IReadOnlyList<string> Ids();

        Task<IReadOnlyList<Instance>> LoadAll(CancellationToken ct = default);

        Task<Instance> Get(string id, CancellationToken ct = default);

        Task<IReadOnlyList<InstanceSummary>> List(CancellationToken ct = default);

        Task<InstanceSummary> Summary(Instance instance, CancellationToken ct = default);

        Task<Instance> Create(CreateInstanceRequest request, OriginType origin = OriginType.Empty,
        CancellationToken ct = default);

        /// <summary>Kopiert eine Instanz samt Welten und Mods. Spielzeit, Logs und Crash-Reports nicht.</summary>
        Task<Instance> Copy(string sourceId, string newName, JobContext job);

        Task<Instance> Update(string id, InstanceUpdate update, CancellationToken ct = default);

        Task Save(Instance instance, CancellationToken ct = default);

        /// <summary>Löschen landet im Papierkorb, damit nichts unwiderruflich weg ist.</summary>
        Task Delete(string id, CancellationToken ct = default);

        /// <summary>
        /// Hintergrund ohne eigenes Bild: Panorama der Version (Blickrichtung 0–3) oder nur Farbverlauf.
        /// Ein eigenes Hintergrundbild wird dabei entfernt.
        /// </summary>
        Task SetBackdrop(string id, bool gradientOnly, int panoramaView, CancellationToken ct = default);

        /// <summary>Speichert Instanzbild oder eigenes Hintergrundbild (intern „banner“). Nur PNG und JPEG, höchstens 5 MB.</summary>
        Task SetImage(string id, bool banner, byte[]? data, CancellationToken ct = default);

        string? ImagePath(string id, bool banner);

        /// <summary>Dateien, die beim Kopieren und Sichern einer Instanz wegfallen (Logs, Caches).</summary>
        bool IsTransient(string relative);
    }
}
