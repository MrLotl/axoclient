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

        public override void Register(MessageRouter router, MainWindow window)
        {
            router.On("accounts.list", () => _accounts.Views());

            router.On<LoginData>("accounts.login", async (request, cancellationToken) =>
            {
                if (request.Type == AccountType.Dev)
                {
                    if (!_devAuth.Enabled)
                    {
                        throw new AxoException("auth.failed");
                    }
                    // Entwicklungs-Provider: Name kommt aus der UI
                    _devAuth.PendingName = TextHelper.Clean(request.DevName, 16) is { Length: >= 3 } devName ? devName : "Entwickler";
                }
                var account = await _accounts.Login(request.Type, cancellationToken);
                _ = refreshOwnSkin();
                _social.Start();
                return new { uuid = account.Uuid, name = account.Name, accounts = _accounts.Views() };
            });

            router.On<UuidData>("accounts.switch", async (request, cancellationToken) =>
            {
                await _accounts.Switch(request.Uuid, cancellationToken);
                _ = refreshOwnSkin();
                _social.Start();
                return _accounts.Views();
            });

            router.On<UuidData>("accounts.remove", async (request, cancellationToken) =>
            {
                await _online.SignOutDevice(request.Uuid, cancellationToken);
                await _accounts.Remove(request.Uuid, cancellationToken);
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
            catch (Exception exception) when (exception is AxoException or HttpRequestException)
            {
                AppLog.Warn("Eigenen Skin laden", exception);
            }
        }
    }
}
