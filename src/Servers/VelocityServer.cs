using AxoClient.Common;
using AxoClient.DataModel.Servers;
using AxoClient.Game.Contract;

namespace AxoClient.Servers
{
    public sealed class VelocityServer : PaperProjectServer
    {
        public VelocityServer(IApiClient api, IGameVersionHelper gameVersionHelper)
            : base(api, "velocity", gameVersionHelper)
        {
        }

        public override ServerSoftware Type => ServerSoftware.Velocity;
        public override bool IsProxy => true;

        public override int RequiredJava(string version)
        {
            return 21;
        }
    }
}
