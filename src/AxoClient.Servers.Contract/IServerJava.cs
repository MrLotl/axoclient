using AxoClient.Common;

namespace AxoClient.Servers.Contract
{
    public interface IServerJava
    {
        Task<string> Ensure(int required, JobContext job);
    }
}
