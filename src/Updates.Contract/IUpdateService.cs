using AxoClient.Common;
using AxoClient.DataModel.Updates;

namespace AxoClient.Updates.Contract
{
    public interface IUpdateService
    {
        UpdateView? Available { get; }

        bool IsInstalled { get; }

        void StartBackgroundChecks();

        Task<UpdateView?> Check(CancellationToken ct = default);

        Task Download(IProgress<double>? progress, CancellationToken ct);

        /// <summary>Installieren und neu starten. Läuft etwas, wird nicht installiert.</summary>
        Task ApplyAndRestart(JobContext job);

        /// <summary>Beim Beenden: heruntergeladenes Update einsetzen, der nächste Start ist dann die neue Version (Automatik).</summary>
        void ApplyOnExitIfReady();
    }
}
