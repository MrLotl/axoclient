using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Diagnostics;
using AxoClient.Diagnostics.Contract;
using AxoClient.Social.Contract;

namespace AxoClient.Diagnostics
{
    public sealed class AccountDiagnostic : IDiagnosticCheck
    {
        private readonly IAccountManager _accounts;
        private readonly IOnlineClient _online;

        public AccountDiagnostic(IAccountManager accounts, IOnlineClient online)
        {
            _accounts = accounts;
            _online = online;
        }

        public string Area => "account";

        public async Task<IReadOnlyList<DiagnosticItem>> Run(CancellationToken cancellationToken)
        {
            if (_accounts.Active == null)
            {
                return [new(Area, DiagnosticStatus.Warning, new TextRef("diagnostics.noAccount"), Solution: new TextRef("diagnostics.noAccount.solution"))];
            }

            var items = new List<DiagnosticItem>();
            try
            {
                await _accounts.GetSession(cancellationToken, forceRefresh: true);
                items.Add(new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.loginOk")));
            }
            catch (AxoException exception)
            {
                items.Add(new(Area, DiagnosticStatus.Error, new TextRef("diagnostics.loginBroken"), exception.Text, new TextRef("diagnostics.loginBroken.solution")));
                return items;
            }
            try
            {
                await _online.Send(HttpMethod.Get, "/me", cancellationToken: cancellationToken);
                items.Add(new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.serviceOk")));
            }
            catch (AxoException exception)
            {
                items.Add(new(Area, DiagnosticStatus.Warning, new TextRef("diagnostics.serviceBroken"), exception.Text));
            }
            return items;
        }
    }
}
