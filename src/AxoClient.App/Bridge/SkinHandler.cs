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

        public override void Register(MessageRouter r, MainWindow window)
        {
            r.On("skins.list", async ct => await _skins.List(ct));

            r.On("skins.profile", async ct =>
            {
                var profile = await _mojang.GetOwnProfile(ct);
                return profile;
            });

            r.On<SkinAddData>("skins.add", async (d, ct) =>
            {
                var bytes = d.FileRef != null ? await File.ReadAllBytesAsync(_files.Resolve(d.FileRef), ct)
                    : d.Base64 != null ? Convert.FromBase64String(d.Base64) : throw new AxoException("errors.invalidInput");
                return await _skins.Add(bytes, d.Name, d.Model, ct: ct);
            });

            r.On<SkinAdoptData>("skins.adopt", async (d, ct) => await _skins.AdoptFromPlayer(d.Player, ct));

            r.On<SkinUpdateData>("skins.update", async (d, ct) =>
            {
                await _skins.Update(d.Id, d.Name, d.Model, d.Base64 == null ? null : Convert.FromBase64String(d.Base64), ct);
                return await _skins.List(ct);
            });

            r.On<IdData>("skins.remove", async (d, ct) =>
            {
                await _skins.Remove(d.Id, ct);
                return await _skins.List(ct);
            });

            r.On<IdData>("skins.apply", async (d, ct) => await _skins.Apply(d.Id, ct));

            r.On<MojangCapeData>("skins.mojangCape", async (d, ct) => await _mojang.SetCape(d.CapeId, ct));

            // ---- Capes ----
            r.On("capes.list", async ct => await _capes.List(ct));

            r.On<CapeSaveData>("capes.save", async (d, ct) =>
                await _capes.Save(d.Id, d.Name, Convert.FromBase64String(d.Base64), d.Source, d.Edit, ct));

            r.On<IdData>("capes.editState", async (d, ct) => await _capes.LoadEditState(d.Id, ct));

            r.On<IdData>("capes.remove", async (d, ct) =>
            {
                await _capes.Remove(d.Id, ct);
                return await _capes.List(ct);
            });

            r.On("capes.official", async ct => await _capeService.Official(ct));

            r.On<AxoCapeData>("capes.setActive", async (d, ct) =>
            {
                await _capeService.SetActive(d.Source is { } s && d.Id != null ? new AxoCapeChoice(s, d.Id) : null, ct);
                return null;
            });

            r.On<IdData>("capes.submit", async (d, ct) =>
            {
                await _capeService.Submit(d.Id, ct);
                return await _capes.List(ct);
            });

            r.On<IdData>("capes.withdraw", async (d, ct) =>
            {
                await _capeService.Withdraw(d.Id, ct);
                return await _capes.List(ct);
            });

            // ---- Admin (Rollen prüft immer der Service, die UI blendet nur aus) ----
            r.On("admin.submissions", async ct => await _capeService.Submissions(ct));
            r.On<ReviewData>("admin.review", async (d, ct) =>
            {
                await _capeService.Review(d.Id, d.Approve, d.Reason, ct);
                return await _capeService.Submissions(ct);
            });
            r.On<OfficialCapeData>("admin.uploadCape", async (d, ct) =>
            {
                await _capeService.UploadOfficial(d.Name, Convert.FromBase64String(d.Base64), ct);
                return null;
            });
            r.On<IdData>("admin.deleteCape", async (d, ct) =>
            {
                await _capeService.DeleteOfficial(d.Id, ct);
                return null;
            });
            r.On("admin.admins", async ct => await _capeService.Admins(ct));
            r.On<AdminData>("admin.setAdmin", async (d, ct) =>
            {
                await _capeService.SetAdmin(d.Uuid, d.Admin, ct);
                return await _capeService.Admins(ct);
            });
        }
    }
}
