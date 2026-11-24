using AxoClient.Launch.Contract;

namespace AxoClient.Integrations.Contract
{
    public interface IGameActivity : IGameLaunchHook
    {
        /// <summary>Aktuelle Adresse zum Nachjoinen (Server oder e4mc), <c>null</c> = Einzelspieler/Menü.</summary>
        string? JoinAddress { get; }

        string? InstanceName { get; }

        string? Minecraft { get; }

        DateTimeOffset? StartedAt { get; }

        event Action<string?>? JoinAddressChanged;

        event Action? Changed;
    }
}
