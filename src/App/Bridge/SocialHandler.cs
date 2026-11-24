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

        public override void Register(MessageRouter router, MainWindow window)
        {
            ISocialService social = _social;

            router.On("social.state", () => new { friends = social.Friends, inbox = social.Inbox, me = social.Me, available = social.Available });
            router.On("social.sync", async cancellationToken =>
            {
                await social.SyncNow(cancellationToken);
                return new { friends = social.Friends, inbox = social.Inbox, me = social.Me, available = social.Available };
            });
            router.On<NameData>("social.request", async (request, cancellationToken) =>
            {
                await social.SendRequest(request.Name, cancellationToken);
                return social.Friends;
            });
            router.On<UuidData>("social.accept", async (request, cancellationToken) =>
            {
                await social.Accept(request.Uuid, cancellationToken);
                return social.Friends;
            });
            router.On<UuidData>("social.decline", async (request, cancellationToken) =>
            {
                await social.Decline(request.Uuid, cancellationToken);
                return social.Friends;
            });
            router.On<UuidData>("social.remove", async (request, cancellationToken) =>
            {
                await social.Remove(request.Uuid, cancellationToken);
                return social.Friends;
            });
            router.On<BlockData>("social.block", async (request, cancellationToken) =>
            {
                await social.Block(request.Uuid, request.Block, cancellationToken);
                return social.Friends;
            });
            router.On("social.blocked", async cancellationToken => await social.LoadBlocked(cancellationToken));
            router.On("social.deleteAccount", async cancellationToken =>
            {
                await social.DeleteAccount(cancellationToken);
                return null;
            });

            // ---- Postfach ----
            router.On<SendInstanceData>("inbox.sendInstance", async (request, cancellationToken) =>
            {
                await _inbox.SendInstance(request.InstanceId, request.Recipients, request.Servers, request.Options, request.ModLock, cancellationToken);
                return null;
            });
            router.On<SendModData>("inbox.sendMod", async (request, cancellationToken) =>
            {
                await _inbox.SendMod(request.Title, new RecipeContent(request.Type, request.Source, request.ProjectId, request.VersionId), request.Recipients, cancellationToken);
                return null;
            });
            router.On<SendServerData>("inbox.sendServer", async (request, cancellationToken) =>
            {
                await _inbox.SendServer(request.Name, request.Address, request.Recipients, cancellationToken);
                return null;
            });
            router.On<InviteData>("inbox.invite", async (request, cancellationToken) =>
            {
                await _inbox.SendInvite(request.Address, request.InstanceName, request.Minecraft, request.Recipients, cancellationToken);
                return null;
            });
            router.On<IdData>("inbox.preview", async (request, cancellationToken) => await _inbox.Fetch(request.Id, cancellationToken));
            router.On<AcceptData>("inbox.accept", async (request, cancellationToken) => await _inbox.Accept(request.Id, request.InstanceId, cancellationToken));
            router.On<IdData>("inbox.discard", async (request, cancellationToken) =>
            {
                await _inbox.Discard(request.Id, cancellationToken);
                return social.Inbox;
            });

            // ---- Benachrichtigungen ----
            router.On("notifications.list", () => new { items = _notifications.All, unread = _notifications.UnreadCount });
            router.On("notifications.read", async cancellationToken =>
            {
                await social.MarkNotificationsRead(cancellationToken);
                return null;
            });
            router.On<IdData>("notifications.remove", (request, _) =>
            {
                _notifications.Remove(request.Id);
                return Task.FromResult<object?>(null);
            });
        }
    }
}
