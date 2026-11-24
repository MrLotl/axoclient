namespace AxoClient.DataModel.Servers
{
    /// <summary>Was aus einem vorhandenen Server-Ordner erkannt wurde – die UI zeigt es im Server-Dialog zur Kontrolle.</summary>
    public sealed record ServerImportInfo(string Name, ServerSoftware Software, string? Version, int Port, int MaxPlayers, bool EulaAccepted,
        int Worlds, int Plugins, int Mods);
}
