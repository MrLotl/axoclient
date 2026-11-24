using AxoClient.Common;
using AxoClient.DataModel.Import;

namespace AxoClient.Import.Contract
{
    public interface IInstanceTransfer
    {
        /// <summary>Andere AxoClient-Instanzen als Quelle (für die Auswahl im Dialog).</summary>
        Task<ForeignLauncherView> OwnSources(string exceptId, CancellationToken cancellationToken);

        Task<ForeignImportResult> Transfer(string sourceId, string targetId, ForeignImportParts parts, JobContext job);
    }
}
