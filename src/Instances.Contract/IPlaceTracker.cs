using AxoClient.DataModel.Instances;
using AxoClient.Launch.Contract;

namespace AxoClient.Instances.Contract
{
    public interface IPlaceTracker : IGameLaunchHook
    {
        Task<PlacesLog> Load(string instanceId, CancellationToken ct = default);
    }
}
