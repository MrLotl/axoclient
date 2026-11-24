namespace AxoClient.DataModel.Accounts
{
    public sealed class AccountEntry
    {
        public string Uuid { get; set; } = "";
        public string Name { get; set; } = "";
        public AccountType Type { get; set; } = AccountType.Microsoft;

        /// <summary>ID des Skins aus der eigenen Bibliothek, falls einer gewählt wurde.</summary>
        public string? Skin { get; set; }
        public AccountCapes Capes { get; set; } = new();
        public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
