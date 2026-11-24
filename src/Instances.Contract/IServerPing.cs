using AxoClient.DataModel.Instances;

namespace AxoClient.Instances.Contract
{
    public interface IServerPing
    {
        Task<ServerStatus> Ping(string address, int timeoutMs = 4000, CancellationToken cancellationToken = default);

        /// <summary>"Play.Example.net" und "play.example.net:25565" gelten als gleich.</summary>
        string Normalize(string address);

        (string Host, int Port) ParseAddress(string address);

        bool IsValidAddress(string? address);
    }
}
