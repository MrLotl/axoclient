using AxoClient.Common;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Launch;

namespace AxoClient.Launch.Contract
{
    public interface ILaunchPipeline
    {
        /// <summary>Führt alle Prüfungen aus. Gibt den vorbereiteten Kontext zurück oder wirft bei Abbruch.</summary>
        Task<LaunchContext> Prepare(string instanceId, QuickPlay? quickPlay, JobContext job);

        /// <summary>
        /// Ob ein Ergebnis im Mods-Tab als "muss repariert werden" zählt. Einrichtungsschritte, die beim Start ohnehin still
        /// laufen (AxoClient-Mod, e4mc, neue Dateien eintragen), schwankende Werte (gerade freier Arbeitsspeicher) und
        /// stummgeschaltete Warnungen gehören nicht dazu – sonst meldet auch ein einwandfreies Modpack ständig ein Problem.
        /// </summary>
        bool IsRepairIssue(CheckResult result, ContentDocument content);

        /// <summary>Für die Diagnose: alle Prüfungen einer Instanz ohne Dialog und ohne Reparatur.</summary>
        Task<IReadOnlyList<CheckResult>> Diagnose(string instanceId, CancellationToken cancellationToken);

        /// <summary>
        /// Ein einzelnes Problem aus der Diagnose beheben. <paramref name="index"/> ist die Stelle in der Liste von
        /// <see cref="Diagnose"/>; die Prüfungen laufen dafür neu, damit die Maßnahme zum aktuellen Stand passt.
        /// </summary>
        Task RepairOne(string instanceId, string checkId, int index, string? actionId, JobContext job);

        /// <summary>"Alles reparieren" im Mods-Tab: für jedes gefundene Problem die erste (empfohlene) Maßnahme ausführen.</summary>
        Task<int> RepairAll(string instanceId, JobContext job);
    }
}
