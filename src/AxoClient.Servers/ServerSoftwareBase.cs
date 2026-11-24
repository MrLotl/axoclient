using AxoClient.Common;
using AxoClient.DataModel.Servers;
using AxoClient.Game.Contract;
using AxoClient.Servers.Contract;

namespace AxoClient.Servers
{
    /// <summary>Gemeinsame Basis: Minecraft-Server brauchen die Java-Version ihrer Minecraft-Version.</summary>
    public abstract class ServerSoftwareBase : IServerSoftware
    {
        private readonly IGameVersionHelper _gameVersionHelper;

        protected ServerSoftwareBase(IGameVersionHelper gameVersionHelper)
        {
            _gameVersionHelper = gameVersionHelper;
        }

        public abstract ServerSoftware Type { get; }

        public virtual bool IsProxy => false;

        public abstract Task<IReadOnlyList<ServerVersionInfo>> ListVersions(CancellationToken ct);

        public abstract Task<DownloadRequest> ResolveJar(string version, string targetPath, CancellationToken ct);

        public virtual int RequiredJava(string version)
        {
            // Java 16 gibt es nicht mehr zum Herunterladen, 17 startet auch 1.17
            var java = _gameVersionHelper.RequiredJava(version);
            return java == 16 ? 17 : java;
        }
    }
}
