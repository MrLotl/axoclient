using AxoClient.Accounts.Contract;
using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;
using AxoClient.Skins.Contract;
using AxoClient.Social.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich Account.</summary>
    public class AccountHandler : BridgeHandler
    {
        private readonly IAccountManager _accounts;
        private readonly IDevAuthProvider _devAuth;
        private readonly ISocialService _social;
        private readonly IOnlineClient _online;
        private readonly IMojangApi _mojang;
        private readonly ISkinManager _skins;

        public AccountHandler(
            IAccountManager accounts,
            IDevAuthProvider devAuth,
            ISocialService social,
            IOnlineClient online,
            IMojangApi mojang,
            ISkinManager skins,
            IAppLog appLog,
            ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _accounts = accounts;
            _devAuth = devAuth;
            _social = social;
            _online = online;
            _mojang = mojang;
            _skins = skins;
        }

        public override void Register(MessageRouter r, MainWindow window)
        {
            r.On("accounts.list", () => _accounts.Views());

            r.On<LoginData>("accounts.login", async (d, ct) =>
            {
                if (d.Type == AccountType.Dev)
                {
                    if (!_devAuth.Enabled)
                    {
                        throw new AxoException("auth.failed");
                    }
                    // Entwicklungs-Provider: Name kommt aus der UI
                    _devAuth.PendingName = TextHelper.Clean(d.DevName, 16) is { Length: >= 3 } n ? n : "Entwickler";
                }
                var account = await _accounts.Login(d.Type, ct);
                _ = refreshOwnSkin();
                _social.Start();
                return new { uuid = account.Uuid, name = account.Name, accounts = _accounts.Views() };
            });

            r.On<UuidData>("accounts.switch", async (d, ct) =>
            {
                await _accounts.Switch(d.Uuid, ct);
                _ = refreshOwnSkin();
                _social.Start();
                return _accounts.Views();
            });

            r.On<UuidData>("accounts.remove", async (d, ct) =>
            {
                await _online.SignOutDevice(d.Uuid, ct);
                await _accounts.Remove(d.Uuid, ct);
                return _accounts.Views();
            });
        }

        /// <summary>Nach Anmeldung oder Wechsel: eigenen Skin für den Kopf oben rechts in den Cache legen.</summary>
        private async Task refreshOwnSkin()
        {
            try
            {
                var profile = await _mojang.GetOwnProfile(CancellationToken.None);
                await _skins.CacheOwnSkin(profile, CancellationToken.None);
            }
            catch (Exception ex) when (ex is AxoException or HttpRequestException)
            {
                AppLog.Warn("Eigenen Skin laden", ex);
            }
        }
    }
}
