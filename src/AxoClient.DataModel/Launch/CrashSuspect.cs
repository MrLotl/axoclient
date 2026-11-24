namespace AxoClient.DataModel.Launch
{
    /// <summary><paramref name="CanRollback"/>: die Mod wurde aktualisiert, die vorige Version liegt noch bereit.</summary>
    public sealed record CrashSuspect(string File, string Name, bool CanRollback = false);
}
