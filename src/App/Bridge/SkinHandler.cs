using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;
using AxoClient.Skins.Contract;
using AxoClient.Social.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich Skin.</summary>
    public class SkinHandler : BridgeHandler
    {
        private readonly ISkinManager _skins;
        private readonly IMojangApi _mojang;
        private readonly ICapeManager _capes;
        private readonly ICapeService _capeService;
        private readonly FileRefs _files;

        public SkinHandler(
            ISkinManager skins,
            IMojangApi mojang,
            ICapeManager capes,
            ICapeService capeService,
            FileRefs files,
            IAppLog appLog,
            ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _skins = skins;
            _mojang = mojang;
            _capes = capes;
            _capeService = capeService;
            _files = files;
        }

        public override void Register(MessageRouter router, MainWindow window)
        {
            router.On("skins.list", async cancellationToken => await _skins.List(cancellationToken));

            router.On("skins.profile", async cancellationToken =>
            {
                var profile = await _mojang.GetOwnProfile(cancellationToken);
                return profile;
            });

            router.On<SkinAddData>("skins.add", async (request, cancellationToken) =>
            {
                var bytes = request.FileRef != null ? await File.ReadAllBytesAsync(_files.Resolve(request.FileRef), cancellationToken)
                    : request.Base64 != null ? Convert.FromBase64String(request.Base64) : throw new AxoException("errors.invalidInput");
                return await _skins.Add(bytes, request.Name, request.Model, cancellationToken: cancellationToken);
            });

            router.On<SkinAdoptData>("skins.adopt", async (request, cancellationToken) => await _skins.AdoptFromPlayer(request.Player, cancellationToken));

            router.On<SkinUpdateData>("skins.update", async (request, cancellationToken) =>
            {
                await _skins.Update(request.Id, request.Name, request.Model, request.Base64 == null ? null : Convert.FromBase64String(request.Base64), cancellationToken);
                return await _skins.List(cancellationToken);
            });

            router.On<IdData>("skins.remove", async (request, cancellationToken) =>
            {
                await _skins.Remove(request.Id, cancellationToken);
                return await _skins.List(cancellationToken);
            });

            router.On<IdData>("skins.apply", async (request, cancellationToken) => await _skins.Apply(request.Id, cancellationToken));

            router.On<MojangCapeData>("skins.mojangCape", async (request, cancellationToken) => await _mojang.SetCape(request.CapeId, cancellationToken));

            // ---- Capes ----
            router.On("capes.list", async cancellationToken => await _capes.List(cancellationToken));

            router.On<CapeSaveData>("capes.save", async (request, cancellationToken) =>
                await _capes.Save(request.Id, request.Name, Convert.FromBase64String(request.Base64), request.Source, request.Edit, cancellationToken));

            router.On<IdData>("capes.editState", async (request, cancellationToken) => await _capes.LoadEditState(request.Id, cancellationToken));

            router.On<IdData>("capes.remove", async (request, cancellationToken) =>
            {
                await _capes.Remove(request.Id, cancellationToken);
                return await _capes.List(cancellationToken);
            });

            router.On("capes.official", async cancellationToken => await _capeService.Official(cancellationToken));

            router.On<AxoCapeData>("capes.setActive", async (request, cancellationToken) =>
            {
                await _capeService.SetActive(request.Source is { } source && request.Id != null ? new AxoCapeChoice(source, request.Id) : null, cancellationToken);
                return null;
            });

            router.On<IdData>("capes.submit", async (request, cancellationToken) =>
            {
                await _capeService.Submit(request.Id, cancellationToken);
                return await _capes.List(cancellationToken);
            });

            router.On<IdData>("capes.withdraw", async (request, cancellationToken) =>
            {
                await _capeService.Withdraw(request.Id, cancellationToken);
                return await _capes.List(cancellationToken);
            });

            // ---- Admin (Rollen prüft immer der Service, die UI blendet nur aus) ----
            router.On("admin.submissions", async cancellationToken => await _capeService.Submissions(cancellationToken));
            router.On<ReviewData>("admin.review", async (request, cancellationToken) =>
            {
                await _capeService.Review(request.Id, request.Approve, request.Reason, cancellationToken);
                return await _capeService.Submissions(cancellationToken);
            });
            router.On<OfficialCapeData>("admin.uploadCape", async (request, cancellationToken) =>
            {
                await _capeService.UploadOfficial(request.Name, Convert.FromBase64String(request.Base64), cancellationToken);
                return null;
            });
            router.On<IdData>("admin.deleteCape", async (request, cancellationToken) =>
            {
                await _capeService.DeleteOfficial(request.Id, cancellationToken);
                return null;
            });
            router.On("admin.admins", async cancellationToken => await _capeService.Admins(cancellationToken));
            router.On<AdminData>("admin.setAdmin", async (request, cancellationToken) =>
            {
                await _capeService.SetAdmin(request.Uuid, request.Admin, cancellationToken);
                return await _capeService.Admins(cancellationToken);
            });
        }
    }
}
