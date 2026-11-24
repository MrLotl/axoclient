using AxoClient.DataModel.Accounts;
using AxoClient.DataModel.Social;

namespace AxoClient.Social.Contract
{
    public interface ICapeService
    {
        Task<IReadOnlyList<OfficialCape>> Official(CancellationToken ct);

        /// <summary>Axo-Cape setzen (offiziell oder eigenes freigegebenes) oder mit <c>null</c> ablegen.</summary>
        Task SetActive(AxoCapeChoice? choice, CancellationToken ct);

        /// <summary>Axo-Cape eines Kontos geändert: (UUID, öffentliche Bild-ID oder <c>null</c>).</summary>
        event Action<string, string?>? ActiveChanged;

        /// <summary>Eigenes Cape zur Veröffentlichung einreichen (ersetzt eine frühere Einreichung).</summary>
        Task Submit(string capeId, CancellationToken ct);

        Task Withdraw(string capeId, CancellationToken ct);

        /// <summary>Ergebnis einer Freigabe aus einer Benachrichtigung übernehmen.</summary>
        Task ApplyReview(string localId, bool approved, string? reason, CancellationToken ct);

        Task<IReadOnlyList<CapeSubmission>> Submissions(CancellationToken ct);

        Task Review(string submissionId, bool approve, string? reason, CancellationToken ct);

        Task UploadOfficial(string name, byte[] png, CancellationToken ct);

        Task DeleteOfficial(string id, CancellationToken ct);

        Task<IReadOnlyList<AdminEntry>> Admins(CancellationToken ct);

        Task SetAdmin(string uuid, bool admin, CancellationToken ct);
    }
}
