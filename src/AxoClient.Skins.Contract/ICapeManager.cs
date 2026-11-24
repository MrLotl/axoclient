using AxoClient.DataModel.Skins;

namespace AxoClient.Skins.Contract
{
    public interface ICapeManager
    {
        Task<IReadOnlyList<CapeView>> List(CancellationToken ct = default);

        string? FilePath(string id);

        Task<CapeView> Save(string? id, string name, byte[] png, CapeSource source, CapeEditState? edit, CancellationToken ct);

        Task<CapeEditState> LoadEditState(string id, CancellationToken ct);

        Task Remove(string id, CancellationToken ct);

        Task SetStatus(string id, CapeStatus status, string? reason, CancellationToken ct = default);

        Task<byte[]> Read(string id, CancellationToken ct);
    }
}
