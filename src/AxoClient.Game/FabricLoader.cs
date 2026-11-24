using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;

namespace AxoClient.Game
{
    public sealed class FabricLoader : MetaProfileLoader
    {
        public FabricLoader(IApiClient api, ILoaderNames loaderNames, IGameVersionHelper gameVersionHelper)
            : base(api, loaderNames, gameVersionHelper)
        {
        }

        public override LoaderType Type => LoaderType.Fabric;
        protected override string MetaUrl => "https://meta.fabricmc.net/v2/versions/loader";
        protected override string ProfilePrefix => "fabric-loader";
    }
}
