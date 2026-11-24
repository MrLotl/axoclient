namespace AxoClient.Common
{
    public interface IActivityTracker
    {
        Task<ActivityLog> Load(string path);

        Task<ActivityLog> AddSession(string path, ActivitySessionKind kind, DateTimeOffset start, DateTimeOffset end);
    }
}
