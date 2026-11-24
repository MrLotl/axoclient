namespace AxoClient.DataModel.Launch
{
    public sealed record RunningGame(string InstanceId, string Name, DateTimeOffset StartedAt, int Pid);
}
