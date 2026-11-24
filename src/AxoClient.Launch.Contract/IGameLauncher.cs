using AxoClient.Common;
using AxoClient.DataModel.Launch;

namespace AxoClient.Launch.Contract
{
    public interface IGameLauncher
    {
        event Action<RunningGame>? GameStarted;

        event Action<string>? GameExited;

        IReadOnlyList<RunningGame> Running { get; }

        bool AnyRunning { get; }

        bool IsRunning(string instanceId);

        /// <summary>Startet eine Instanz als Job. Fortschritt und Dialoge laufen über die Bridge.</summary>
        Task Launch(string instanceId, QuickPlay? quickPlay = null);

        Task Stop(string instanceId);

        /// <summary>Letzte Log-Zeilen einer Instanz (laufend oder gerade beendet), für Konsole und Absturz-Dialog.</summary>
        IReadOnlyList<LogLine> LogOf(string instanceId, int max = 5000);
    }
}
