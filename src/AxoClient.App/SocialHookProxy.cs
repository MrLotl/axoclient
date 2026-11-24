using AxoClient.Common;
using AxoClient.DataModel.Launch;
using AxoClient.Launch.Contract;

namespace AxoClient.App
{
    /// <summary>Der SocialService braucht den GameLauncher und umgekehrt; dieser Stellvertreter löst den Kreis.</summary>
    public sealed class SocialHookProxy : IGameLaunchHook
    {
        public IGameLaunchHook? Target { get; set; }

        void IGameLaunchHook.OnStarted(LaunchContext ctx, RunningProcess process)
        {
            Target?.OnStarted(ctx, process);
        }

        void IGameLaunchHook.OnExited(LaunchContext ctx, RunningProcess process, int exitCode, bool crashed)
        {
            Target?.OnExited(ctx, process, exitCode, crashed);
        }
    }
}
