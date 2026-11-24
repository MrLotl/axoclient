using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.Launch.Contract;

namespace AxoClient.App
{
    /// <summary>Der SocialService braucht den GameLauncher und umgekehrt; dieser Stellvertreter löst den Kreis.</summary>
    public sealed class SocialHookProxy : IGameLaunchHook
    {
        public IGameLaunchHook? Target { get; set; }

        void IGameLaunchHook.OnStarted(LaunchContext context, RunningProcess process)
        {
            Target?.OnStarted(context, process);
        }

        void IGameLaunchHook.OnExited(LaunchContext context, RunningProcess process, int exitCode, bool crashed)
        {
            Target?.OnExited(context, process, exitCode, crashed);
        }
    }
}
