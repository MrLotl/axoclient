using AxoClient.DataModel.Accounts;
using AxoClient.DataModel.Social;

namespace AxoClient.Social.Contract
{
    public interface ICapeService
    {
        Task<IReadOnlyList<OfficialCape>> Official(CancellationToken cancellationToken);

        /// <summary>Axo-Cape setzen (offiziell oder eigenes freigegebenes) oder mit <c>null</c> ablegen.</summary>
        Task SetActive(AxoCapeChoice? choice, CancellationToken cancellationToken);

        /// <summary>Axo-Cape eines Kontos geändert: (UUID, öffentliche Bild-ID oder <c>null</c>).</summary>
        event Action<string, string?>? ActiveChanged;

        /// <summary>Eigenes Cape zur Veröffentlichung einreichen (ersetzt eine frühere Einreichung).</summary>
        Task Submit(string capeId, CancellationToken cancellationToken);

        Task Withdraw(string capeId, CancellationToken cancellationToken);

        /// <summary>Ergebnis einer Freigabe aus einer Benachrichtigung übernehmen.</summary>
        Task ApplyReview(string localId, bool approved, string? reason, CancellationToken cancellationToken);

        Task<IReadOnlyList<CapeSubmission>> Submissions(CancellationToken cancellationToken);

        Task Review(string submissionId, bool approve, string? reason, CancellationToken cancellationToken);

        Task UploadOfficial(string name, byte[] png, CancellationToken cancellationToken);

        Task DeleteOfficial(string id, CancellationToken cancellationToken);

        Task<IReadOnlyList<AdminEntry>> Admins(CancellationToken cancellationToken);

        Task SetAdmin(string uuid, bool admin, CancellationToken cancellationToken);
    }
}
