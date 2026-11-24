using AxoClient.DataModel.Skins;

namespace AxoClient.Skins.Contract
{
    public interface ICapeManager
    {
        Task<IReadOnlyList<CapeView>> List(CancellationToken cancellationToken = default);

        string? FilePath(string id);

        Task<CapeView> Save(string? id, string name, byte[] png, CapeSource source, CapeEditState? edit, CancellationToken cancellationToken);

        Task<CapeEditState> LoadEditState(string id, CancellationToken cancellationToken);

        Task Remove(string id, CancellationToken cancellationToken);

        Task SetStatus(string id, CapeStatus status, string? reason, CancellationToken cancellationToken = default);

        Task<byte[]> Read(string id, CancellationToken cancellationToken);
    }
}
