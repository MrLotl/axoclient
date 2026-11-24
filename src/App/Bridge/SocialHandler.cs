using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.DataModel.Social;
using AxoClient.Social.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich Social.</summary>
    public class SocialHandler : BridgeHandler
    {
        private readonly ISocialService _social;
        private readonly IInboxManager _inbox;
        private readonly INotificationManager _notifications;

        public SocialHandler(
            ISocialService social,
            IInboxManager inbox,
            INotificationManager notifications,
            IAppLog appLog,
            ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _social = social;
            _inbox = inbox;
            _notifications = notifications;
        }

        public override void Register(MessageRouter r, MainWindow window)
        {
            ISocialService social = _social;

            r.On("social.state", () => new { friends = social.Friends, inbox = social.Inbox, me = social.Me, available = social.Available });
            r.On("social.sync", async ct =>
            {
                await social.SyncNow(ct);
                return new { friends = social.Friends, inbox = social.Inbox, me = social.Me, available = social.Available };
            });
            r.On<NameData>("social.request", async (d, ct) =>
            {
                await social.SendRequest(d.Name, ct);
                return social.Friends;
            });
            r.On<UuidData>("social.accept", async (d, ct) =>
            {
                await social.Accept(d.Uuid, ct);
                return social.Friends;
            });
            r.On<UuidData>("social.decline", async (d, ct) =>
            {
                await social.Decline(d.Uuid, ct);
                return social.Friends;
            });
            r.On<UuidData>("social.remove", async (d, ct) =>
            {
                await social.Remove(d.Uuid, ct);
                return social.Friends;
            });
            r.On<BlockData>("social.block", async (d, ct) =>
            {
                await social.Block(d.Uuid, d.Block, ct);
                return social.Friends;
            });
            r.On("social.blocked", async ct => await social.LoadBlocked(ct));
            r.On("social.deleteAccount", async ct =>
            {
                await social.DeleteAccount(ct);
                return null;
            });

            // ---- Postfach ----
            r.On<SendInstanceData>("inbox.sendInstance", async (d, ct) =>
            {
                await _inbox.SendInstance(d.InstanceId, d.Recipients, d.Servers, d.Options, d.ModLock, ct);
                return null;
            });
            r.On<SendModData>("inbox.sendMod", async (d, ct) =>
            {
                await _inbox.SendMod(d.Title, new RecipeContent(d.Type, d.Source, d.ProjectId, d.VersionId), d.Recipients, ct);
                return null;
            });
            r.On<SendServerData>("inbox.sendServer", async (d, ct) =>
            {
                await _inbox.SendServer(d.Name, d.Address, d.Recipients, ct);
                return null;
            });
            r.On<InviteData>("inbox.invite", async (d, ct) =>
            {
                await _inbox.SendInvite(d.Address, d.InstanceName, d.Minecraft, d.Recipients, ct);
                return null;
            });
            r.On<IdData>("inbox.preview", async (d, ct) => await _inbox.Fetch(d.Id, ct));
            r.On<AcceptData>("inbox.accept", async (d, ct) => await _inbox.Accept(d.Id, d.InstanceId, ct));
            r.On<IdData>("inbox.discard", async (d, ct) =>
            {
                await _inbox.Discard(d.Id, ct);
                return social.Inbox;
            });

            // ---- Benachrichtigungen ----
            r.On("notifications.list", () => new { items = _notifications.All, unread = _notifications.UnreadCount });
            r.On("notifications.read", async ct =>
            {
                await social.MarkNotificationsRead(ct);
                return null;
            });
            r.On<IdData>("notifications.remove", (d, _) =>
            {
                _notifications.Remove(d.Id);
                return Task.FromResult<object?>(null);
            });
        }
    }
}
