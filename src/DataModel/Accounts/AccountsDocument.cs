using AxoClient.Common;

namespace AxoClient.DataModel.Accounts
{
    /// <summary><c>accounts.json</c>: nur Namen und Auswahl, Tokens liegen im <see cref="TokenStore"/>.</summary>
    public sealed class AccountsDocument : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public string? Active { get; set; }
        public List<AccountEntry> Accounts { get; set; } = [];
    }
}
