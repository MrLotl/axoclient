using AxoClient.Core.Accounts;
using AxoClient.Core.Common;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record LoginData(AccountType Type = AccountType.Microsoft, string? DevName = null);

    private sealed record UuidData(string Uuid);

    private static void RegisterAccounts(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;

        r.On("accounts.list", () => host.Accounts.Views());

        r.On<LoginData>("accounts.login", async (d, ct) =>
        {
            if (d.Type == AccountType.Dev)
            {
                if (!DevAuthProvider.Enabled)
                    throw new AxoException("auth.failed");
                // Entwicklungs-Provider: Name kommt aus der UI
                host.DevAuth.PendingName = Text.Clean(d.DevName, 16) is { Length: >= 3 } n ? n : "Entwickler";
            }
            var account = await host.Accounts.LoginAsync(d.Type, ct);
            _ = RefreshOwnSkinAsync(host);
            host.Social.Start();
            return new { uuid = account.Uuid, name = account.Name, accounts = host.Accounts.Views() };
        });

        r.On<UuidData>("accounts.switch", async (d, ct) =>
        {
            await host.Accounts.SwitchAsync(d.Uuid, ct);
            _ = RefreshOwnSkinAsync(host);
            host.Social.Start();
            return host.Accounts.Views();
        });

        r.On<UuidData>("accounts.remove", async (d, ct) =>
        {
            await host.Online.SignOutDeviceAsync(d.Uuid, ct);
            await host.Accounts.RemoveAsync(d.Uuid, ct);
            return host.Accounts.Views();
        });
    }

    /// <summary>Nach Anmeldung oder Wechsel: eigenen Skin für den Kopf oben rechts in den Cache legen.</summary>
    private static async Task RefreshOwnSkinAsync(AppHost host)
    {
        try
        {
            var profile = await host.Mojang.GetOwnProfileAsync(CancellationToken.None);
            await host.Skins.CacheOwnSkinAsync(profile, CancellationToken.None);
        }
        catch (Exception ex) when (ex is AxoException or HttpRequestException)
        {
            AppLog.Warn("Eigenen Skin laden", ex);
        }
    }
}
