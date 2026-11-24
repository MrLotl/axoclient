using AxoClient.Common;
using AxoClient.DataModel.Content;

namespace AxoClient.Content.Contract
{
    public interface IUpdateScanner
    {
        Task<IReadOnlyList<ContentUpdate>> Scan(string instanceId, CancellationToken ct);

        Task Apply(string instanceId, IReadOnlyList<ContentUpdate> updates, JobContext job);
    }
}
