using AxoClient.DataModel.Servers;

namespace AxoClient.App.Bridge.Requests
{
    public sealed record SoftwareData(ServerSoftware Software);

    public sealed record ServerSaveData(string? Id, string Name, ServerSoftware Software, string Version, int Port, int MaxPlayers, int MemoryMb,
            bool OpenToInternet, bool EulaAccepted, string? ImportRef = null);

    public sealed record ServerImageData(string Id, bool Banner, string? Base64);
}
