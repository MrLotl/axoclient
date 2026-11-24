using AxoClient.DataModel.Accounts;

namespace AxoClient.App.Bridge.Requests
{
    public sealed record LoginData(AccountType Type = AccountType.Microsoft, string? DevName = null);

    public sealed record UuidData(string Uuid);
}
