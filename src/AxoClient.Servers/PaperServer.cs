using AxoClient.Common;
using AxoClient.DataModel.Servers;
using AxoClient.Game.Contract;

namespace AxoClient.Servers
{
    public sealed class PaperServer : PaperProjectServer
    {
        public PaperServer(IApiClient api, IGameVersionHelper gameVersionHelper)
            : base(api, "paper", gameVersionHelper)
        {
        }

        public override ServerSoftware Type => ServerSoftware.Paper;
    }
}
