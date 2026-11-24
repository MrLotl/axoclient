using AxoClient.Common;
using AxoClient.DataModel.Content;

namespace AxoClient.Content.Contract
{
    public interface IDatapackManager
    {
        Task<IReadOnlyList<DatapackItem>> List(string instanceId, string world, CancellationToken cancellationToken = default);

        /// <summary>Passende Version (Datapack-Loader, Minecraft-Version der Instanz) laden; ersetzt eine ältere desselben Projekts.</summary>
        Task<string> Install(string instanceId, string world, InstallRequest request, JobContext job);

        Task SetEnabled(string instanceId, string world, string file, bool enabled);

        /// <summary>In den Papierkorb, wie bei Mods.</summary>
        Task Remove(string instanceId, string world, string file, CancellationToken cancellationToken = default);
    }
}
