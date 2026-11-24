namespace AxoClient.Accounts.Contract
{
    public interface IDevAuthProvider : IAuthProvider
    {
        bool Enabled { get; }

        string PendingName { get; set; }

        /// <summary>Wie Minecraft: UUID v3 aus "OfflinePlayer:&lt;name&gt;".</summary>
        string OfflineUuid(string name);
    }
}
