namespace AxoClient.DataModel.Servers
{
    public sealed record ServerRequest(
        string Name,
        ServerSoftware Software,
        string Version,
        int Port,
        int MaxPlayers,
        int MemoryMb,
        bool OpenToInternet,
        bool EulaAccepted);
}
