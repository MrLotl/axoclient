using AxoClient.Common;
using AxoClient.DataModel.Servers;

namespace AxoClient.Servers.Contract
{
    /// <summary>Eine Server-Software: Versionen, Download der Server-Jar, benötigte Java-Version.</summary>
    public interface IServerSoftware
    {
        ServerSoftware Type { get; }

        /// <summary>Proxy statt Minecraft-Server (Velocity: keine EULA, keine Welten).</summary>
        bool IsProxy { get; }

        Task<IReadOnlyList<ServerVersionInfo>> ListVersions(CancellationToken cancellationToken);

        Task<DownloadRequest> ResolveJar(string version, string targetPath, CancellationToken cancellationToken);

        int RequiredJava(string version);
    }
}
