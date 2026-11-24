namespace AxoClient.DataModel.Accounts
{
    /// <summary>Was die UI über einen Account sieht.</summary>
    public sealed record AccountView(string Uuid, string Name, AccountType Type, bool Active, string HeadUrl, bool NeedsLogin);
}
