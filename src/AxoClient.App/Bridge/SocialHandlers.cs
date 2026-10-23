using AxoClient.Core.Social;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record NameData(string Name);

    private sealed record BlockData(string Uuid, bool Block);

    private sealed record SendInstanceData(string InstanceId, List<string> Recipients, bool Servers = true, bool Options = false, bool ModLock = true);

    private sealed record SendModData(string Title, string Type, string Source, string ProjectId, string VersionId, List<string> Recipients);

    private sealed record SendServerData(string Name, string Address, List<string> Recipients);

    private sealed record InviteData(string Address, string? InstanceName, string? Minecraft, List<string> Recipients);

    private sealed record AcceptData(string Id, string? InstanceId);

    private static void RegisterSocial(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;
        var social = host.Social;

        r.On("social.state", () => new { friends = social.Friends, inbox = social.Inbox, me = social.Me, available = social.Available });
        r.On("social.sync", async ct =>
        {
            await social.SyncNowAsync(ct);
            return new { friends = social.Friends, inbox = social.Inbox, me = social.Me, available = social.Available };
        });
        r.On<NameData>("social.request", async (d, ct) =>
        {
            await social.SendRequestAsync(d.Name, ct);
            return social.Friends;
        });
        r.On<UuidData>("social.accept", async (d, ct) =>
        {
            await social.AcceptAsync(d.Uuid, ct);
            return social.Friends;
        });
        r.On<UuidData>("social.decline", async (d, ct) =>
        {
            await social.DeclineAsync(d.Uuid, ct);
            return social.Friends;
        });
        r.On<UuidData>("social.remove", async (d, ct) =>
        {
            await social.RemoveAsync(d.Uuid, ct);
            return social.Friends;
        });
        r.On<BlockData>("social.block", async (d, ct) =>
        {
            await social.BlockAsync(d.Uuid, d.Block, ct);
            return social.Friends;
        });
        r.On("social.blocked", async ct => await social.LoadBlockedAsync(ct));
        r.On("social.deleteAccount", async ct =>
        {
            await social.DeleteAccountAsync(ct);
            return null;
        });

        // ---- Postfach ----
        r.On<SendInstanceData>("inbox.sendInstance", async (d, ct) =>
        {
            await host.Inbox.SendInstanceAsync(d.InstanceId, d.Recipients, d.Servers, d.Options, d.ModLock, ct);
            return null;
        });
        r.On<SendModData>("inbox.sendMod", async (d, ct) =>
        {
            await host.Inbox.SendModAsync(d.Title, new RecipeContent(d.Type, d.Source, d.ProjectId, d.VersionId), d.Recipients, ct);
            return null;
        });
        r.On<SendServerData>("inbox.sendServer", async (d, ct) =>
        {
            await host.Inbox.SendServerAsync(d.Name, d.Address, d.Recipients, ct);
            return null;
        });
        r.On<InviteData>("inbox.invite", async (d, ct) =>
        {
            await host.Inbox.SendInviteAsync(d.Address, d.InstanceName, d.Minecraft, d.Recipients, ct);
            return null;
        });
        r.On<IdData>("inbox.preview", async (d, ct) => await host.Inbox.FetchAsync(d.Id, ct));
        r.On<AcceptData>("inbox.accept", async (d, ct) => await host.Inbox.AcceptAsync(d.Id, d.InstanceId, ct));
        r.On<IdData>("inbox.discard", async (d, ct) =>
        {
            await host.Inbox.DiscardAsync(d.Id, ct);
            return social.Inbox;
        });

        // ---- Benachrichtigungen ----
        r.On("notifications.list", () => new { items = host.Notifications.All, unread = host.Notifications.UnreadCount });
        r.On("notifications.read", async ct =>
        {
            await social.MarkNotificationsReadAsync(ct);
            return null;
        });
        r.On<IdData>("notifications.remove", (d, _) =>
        {
            host.Notifications.Remove(d.Id);
            return Task.FromResult<object?>(null);
        });
    }
}
