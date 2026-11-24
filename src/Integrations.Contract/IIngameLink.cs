namespace AxoClient.Integrations.Contract
{
    public interface IIngameLink
    {
        void Start();

        /// <summary>
        /// Fehlende Mods installieren (aus dem Dialog im Launcher) und danach das Spiel neu starten, direkt in die Welt des Freundes.
        /// </summary>
        Task InstallMissing(string instanceId, string address, IReadOnlyList<(string Source, string ProjectId, string? VersionId)> mods);

        /// <summary>e4mc-Adressen (offene Welt über das Relais) erkennt man am Namen.</summary>
        bool IsRelay(string address);
    }
}
