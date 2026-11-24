using AxoClient.Common;

namespace AxoClient.Launch.Contract
{
    public interface ICrashActions
    {
        Task Run(string instanceId, string action, string? file, JobContext job);
    }
}
