using AxoClient.Common;
using AxoClient.DataModel.Launch;

namespace AxoClient.Launch.Contract
{
    /// <summary>Erweiterungen rund um den Spielstart (AxoMod-Verbindung, Discord-Status, Online-Status …).</summary>
    public interface IGameLaunchHook
    {
        /// <summary>Zusätzliche JVM-Argumente für diesen Start.</summary>
        IEnumerable<string> JvmArguments(LaunchContext ctx) => [];

        void OnStarted(LaunchContext ctx, RunningProcess process)
        {
        }

        void OnExited(LaunchContext ctx, RunningProcess process, int exitCode, bool crashed)
        {
        }
    }
}
