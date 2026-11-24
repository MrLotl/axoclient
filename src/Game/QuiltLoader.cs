using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;

namespace AxoClient.Game
{
    public sealed class QuiltLoader : MetaProfileLoader
    {
        public QuiltLoader(IApiClient api, ILoaderNames loaderNames, IGameVersionHelper gameVersionHelper)
            : base(api, loaderNames, gameVersionHelper)
        {
        }

        public override LoaderType Type => LoaderType.Quilt;
        protected override string MetaUrl => "https://meta.quiltmc.org/v3/versions/loader";
        protected override string ProfilePrefix => "quilt-loader";

        /// <summary>Quilt markiert Betas nur über "-beta" im Namen; neue MC-Versionen haben oft nur Betas.</summary>
        protected override string? PickRecommended(string minecraftVersion, List<(string Version, bool Stable)> versions) =>
            versions.FirstOrDefault(version => !version.Version.Contains('-')).Version ?? versions.FirstOrDefault().Version;
    }
}
