using System.Security.Cryptography;
using System.Text;
using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;

namespace AxoClient.Accounts
{
    /// <summary>
    /// Offline-Konto nur für die Entwicklung (Umgebungsvariable <c>AXOCLIENT_DEV=1</c>). Startet das Spiel ohne
    /// Microsoft-Konto, Server im Online-Modus lassen damit nicht beitreten.
    /// </summary>
    public sealed class DevAuthProvider : IAuthProvider, IDevAuthProvider
    {
        private readonly ITextHelper _textHelper;
        private readonly IUuidHelper _uuidHelper;
        public DevAuthProvider(ITextHelper textHelper, IUuidHelper uuidHelper)
        {
            _textHelper = textHelper;
            _uuidHelper = uuidHelper;
        }

        public bool Enabled => Environment.GetEnvironmentVariable("AXOCLIENT_DEV") == "1";

        public AccountType Type => AccountType.Dev;

        public string PendingName { get; set; } = "Entwickler";

        public Task<GameSession> Login(CancellationToken cancellationToken)
        {
            if (!Enabled)
            {
                throw new AxoException("auth.failed");
            }

            var name = _textHelper.Clean(PendingName, 16);
            return Task.FromResult(new GameSession(OfflineUuid(name), name, "0", null, AccountType.Dev));
        }

        public Task<GameSession> Refresh(string uuid, CancellationToken cancellationToken) =>
            Task.FromException<GameSession>(new AxoException("auth.expired"));

        public Task SignOut(string uuid, CancellationToken cancellationToken) => Task.CompletedTask;

        public bool HasStoredLogin(string uuid) => Enabled;

        /// <summary>Wie Minecraft: UUID v3 aus "OfflinePlayer:&lt;name&gt;".</summary>
        public string OfflineUuid(string name)
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
            hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
            return _uuidHelper.FormatUuid(Convert.ToHexString(hash));
        }
    }
}
