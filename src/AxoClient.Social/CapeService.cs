using System.Net.Http.Headers;
using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;
using AxoClient.DataModel.Skins;
using AxoClient.DataModel.Social;
using AxoClient.Skins.Contract;
using AxoClient.Social.Contract;

namespace AxoClient.Social
{
    /// <summary>Axo-Capes beim Online-Service: offizielle Capes, eigenes aktives Cape, Einreichungen und Admin-Freigabe.</summary>
    public sealed class CapeService : ICapeService
    {
        private readonly IOnlineClient _client;
        private readonly ICapeManager _capes;
        private readonly IAccountManager _accounts;
        private readonly ITextHelper _textHelper;
        private readonly ISkinFiles _skinFiles;
        private readonly IUuidHelper _uuidHelper;

        public async Task<IReadOnlyList<OfficialCape>> Official(CancellationToken ct) =>
            await _client.Send<List<OfficialCape>>(HttpMethod.Get, "/capes", ct: ct) ?? [];

        /// <summary>Axo-Cape setzen (offiziell oder eigenes freigegebenes) oder mit <c>null</c> ablegen.</summary>
        public async Task SetActive(AxoCapeChoice? choice, CancellationToken ct)
        {
            // Der Dienst antwortet mit der öffentlichen Bild-ID (bei eigenen Capes nicht die ID im Launcher)
            var result = await _client.Send<ActiveCapeResponse>(HttpMethod.Put, "/capes/active", new
            {
                kind = choice?.Source.ToString().ToLowerInvariant(),
                id = choice?.Id
            }, ct);
            if (_accounts.Active is { } account)
            {
                await _accounts.UpdateEntry(account.Uuid, a => a.Capes.Axo = choice);
                ActiveChanged?.Invoke(account.Uuid, choice == null ? null : result?.Cape ?? choice.Id);
            }
        }

        private sealed record ActiveCapeResponse(string? Cape);

        /// <summary>Axo-Cape eines Kontos geändert: (UUID, öffentliche Bild-ID oder <c>null</c>).</summary>
        public event Action<string, string?>? ActiveChanged;

        public CapeService(
            IOnlineClient client,
            ICapeManager capes,
            IAccountManager accounts,
            ITextHelper textHelper,
            ISkinFiles skinFiles,
            IUuidHelper uuidHelper)
        {
            _client = client;
            _capes = capes;
            _accounts = accounts;
            _textHelper = textHelper;
            _skinFiles = skinFiles;
            _uuidHelper = uuidHelper;
        }

        /// <summary>Eigenes Cape zur Veröffentlichung einreichen (ersetzt eine frühere Einreichung).</summary>
        public async Task Submit(string capeId, CancellationToken ct)
        {
            var view = (await _capes.List(ct)).FirstOrDefault(c => c.Id == capeId) ?? throw new AxoException("errors.notFound");
            var png = await _capes.Read(capeId, ct);
            if (!_skinFiles.IsValidCape(png))
            {
                throw new AxoException("capes.invalidImage");
            }

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(view.Name), "name");
            form.Add(new StringContent(capeId), "localId");
            var file = new ByteArrayContent(png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "cape.png");
            using var _ = await _client.SendRaw(HttpMethod.Post, "/capes/submissions", form, ct);
            // Es gibt höchstens eine Einreichung: alle anderen gelten wieder als lokal
            foreach (var other in (await _capes.List(ct)).Where(c => c.Status == CapeStatus.Pending && c.Id != capeId))
            {
                await _capes.SetStatus(other.Id, CapeStatus.Local, null, ct);
            }

            await _capes.SetStatus(capeId, CapeStatus.Pending, null, ct);
        }

        public async Task Withdraw(string capeId, CancellationToken ct)
        {
            await _client.Send(HttpMethod.Delete, "/capes/submissions", ct: ct);
            await _capes.SetStatus(capeId, CapeStatus.Local, null, ct);
        }

        /// <summary>Ergebnis einer Freigabe aus einer Benachrichtigung übernehmen.</summary>
        public async Task ApplyReview(string localId, bool approved, string? reason, CancellationToken ct)
        {
            if (_textHelper.IsValidId(localId))
            {
                await _capes.SetStatus(localId, approved ? CapeStatus.Approved : CapeStatus.Rejected, reason, ct);
            }
        }

        // ---- Admin ----

        public async Task<IReadOnlyList<CapeSubmission>> Submissions(CancellationToken ct) =>
            await _client.Send<List<CapeSubmission>>(HttpMethod.Get, "/admin/submissions", ct: ct) ?? [];

        public Task Review(string submissionId, bool approve, string? reason, CancellationToken ct) =>
            _client.Send(HttpMethod.Post, $"/admin/submissions/{Uri.EscapeDataString(submissionId)}",
                new { approve, reason = reason == null ? null : _textHelper.Clean(reason, 200) }, ct);

        public async Task UploadOfficial(string name, byte[] png, CancellationToken ct)
        {
            if (!_skinFiles.IsValidCape(png))
            {
                throw new AxoException("capes.invalidImage");
            }

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(_textHelper.Clean(name, 40)), "name");
            var file = new ByteArrayContent(png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "cape.png");
            using var _ = await _client.SendRaw(HttpMethod.Post, "/admin/capes", form, ct);
        }

        public Task DeleteOfficial(string id, CancellationToken ct) =>
            _client.Send(HttpMethod.Delete, $"/admin/capes/{Uri.EscapeDataString(id)}", ct: ct);

        public async Task<IReadOnlyList<AdminEntry>> Admins(CancellationToken ct) =>
            await _client.Send<List<AdminEntry>>(HttpMethod.Get, "/admin/admins", ct: ct) ?? [];

        public Task SetAdmin(string uuid, bool admin, CancellationToken ct) =>
            _client.Send(admin ? HttpMethod.Put : HttpMethod.Delete, $"/admin/admins/{_uuidHelper.RequireUuid(uuid)}", ct: ct);
    }
}
