using AxoClient.DataModel.Servers;

namespace AxoClient.Servers.Contract
{
    public interface IPortForwarding
    {
        string? LanIp();

        Task<PortMapping> Open(int port, string description, CancellationToken ct);

        Task Close(PortMapping mapping);
    }
}
