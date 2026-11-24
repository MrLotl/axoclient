using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;
using AxoClient.DataModel.Launch;

namespace AxoClient.Launch.Checks
{
    /// <summary>Konto: Token abgelaufen → still erneuern; schlägt das fehl → Blocker "Neu anmelden".</summary>
    public sealed class AccountCheck : LaunchCheck
    {
        private readonly IAccountManager _accounts;

        public AccountCheck(IAccountManager accounts)
        {
            _accounts = accounts;
        }

        public override string Id => "account";
        public override LaunchPhase Phase => LaunchPhase.Environment;

        public override async Task<IReadOnlyList<CheckResult>> Run(LaunchContext ctx, CancellationToken ct)
        {
            if (_accounts.Active == null)
            {
                return [new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.account.none"),
                    [new CheckAction("login", new TextRef("pipeline.action.login"), login, true)], CanSilence: false)];
            }

            try
            {
                ctx.Session = await _accounts.GetSession(ct);
                return [Ok()];
            }
            catch (AxoException ex) when (ex.Code == "errors.offline")
            {
                // Offline: Microsoft-Spiele starten trotzdem (nur Einzelspieler sinnvoll)
                ctx.Session = _accounts.CachedSession;
                ctx.Offline = true;
                return ctx.Session != null
                    ? [Ok()]
                    : [new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.account.offlineNoSession"),
                        [new CheckAction("retry", new TextRef("pipeline.action.retry"), (_, _) => Task.CompletedTask, true)], CanSilence: false)];
            }
            catch (AxoException ex) when (ex.Code == "auth.expired")
            {
                return [new CheckResult(Id, Severity.Blocker, new TextRef("pipeline.account.expired"),
                    [new CheckAction("login", new TextRef("pipeline.action.login"), login, true)], CanSilence: false)];
            }
        }

        private async Task login(LaunchContext ctx, JobContext job)
        {
            var type = _accounts.Active?.Type ?? AccountType.Microsoft;
            await _accounts.Login(type, job.Token);
            ctx.Session = await _accounts.GetSession(job.Token);
        }
    }
}
