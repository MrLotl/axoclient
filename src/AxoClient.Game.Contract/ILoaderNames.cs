using AxoClient.DataModel.Game;

namespace AxoClient.Game.Contract
{
    public interface ILoaderNames
    {
        string Display(LoaderType type);

        /// <summary>Bezeichnung wie bei Modrinth (<c>loaders</c>-Filter).</summary>
        string ModrinthId(LoaderType type);

        bool SupportsMods(LoaderType type);
    }
}
