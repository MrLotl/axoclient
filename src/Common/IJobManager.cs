namespace AxoClient.Common
{
    public interface IJobManager
    {
        IReadOnlyList<JobInfo> Active { get; }

        bool IsRunning(string kind, string? subject = null);

        Task<T> Run<T>(string kind, TextRef title, Func<JobContext, Task<T>> work, string? subject = null,
        CancellationToken outer = default);

        Task Run(string kind, TextRef title, Func<JobContext, Task> work, string? subject = null,
        CancellationToken outer = default);

        bool Cancel(string jobId);
    }
}
