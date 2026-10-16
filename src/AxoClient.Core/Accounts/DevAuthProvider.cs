using System.Security.Cryptography;
using System.Text;
using AxoClient.Core.Common;

namespace AxoClient.Core.Accounts;

/// <summary>
/// Offline-Konto nur für die Entwicklung (Umgebungsvariable <c>AXOCLIENT_DEV=1</c>). Startet das Spiel ohne
/// Microsoft-Konto, Server im Online-Modus lassen damit nicht beitreten.
/// </summary>
public sealed class DevAuthProvider : IAuthProvider
{
    public static bool Enabled => Environment.GetEnvironmentVariable("AXOCLIENT_DEV") == "1";

    public AccountType Type => AccountType.Dev;

    public string PendingName { get; set; } = "Entwickler";

    public Task<GameSession> LoginAsync(CancellationToken ct)
    {
        if (!Enabled)
            throw new AxoException("auth.failed");
        var name = Text.Clean(PendingName, 16);
        return Task.FromResult(new GameSession(OfflineUuid(name), name, "0", null, AccountType.Dev));
    }

    public Task<GameSession> RefreshAsync(string uuid, CancellationToken ct) =>
        Task.FromException<GameSession>(new AxoException("auth.expired"));

    public Task SignOutAsync(string uuid, CancellationToken ct) => Task.CompletedTask;

    public bool HasStoredLogin(string uuid) => Enabled;

    /// <summary>Wie Minecraft: UUID v3 aus "OfflinePlayer:&lt;name&gt;".</summary>
    public static string OfflineUuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return MicrosoftAuthProvider.FormatUuid(Convert.ToHexString(hash));
    }
}
