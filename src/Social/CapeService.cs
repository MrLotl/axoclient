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

        public async Task<IReadOnlyList<OfficialCape>> Official(CancellationToken cancellationToken) =>
            await _client.Send<List<OfficialCape>>(HttpMethod.Get, "/capes", cancellationToken: cancellationToken) ?? [];

        /// <summary>Axo-Cape setzen (offiziell oder eigenes freigegebenes) oder mit <c>null</c> ablegen.</summary>
        public async Task SetActive(AxoCapeChoice? choice, CancellationToken cancellationToken)
        {
            // Der Dienst antwortet mit der öffentlichen Bild-ID (bei eigenen Capes nicht die ID im Launcher)
            var result = await _client.Send<ActiveCapeResponse>(HttpMethod.Put, "/capes/active", new
            {
                kind = choice?.Source.ToString().ToLowerInvariant(),
                id = choice?.Id
            }, cancellationToken);
            if (_accounts.Active is { } account)
            {
                await _accounts.UpdateEntry(account.Uuid, accountEntry => accountEntry.Capes.Axo = choice);
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
        public async Task Submit(string capeId, CancellationToken cancellationToken)
        {
            var view = (await _capes.List(cancellationToken)).FirstOrDefault(capeView => capeView.Id == capeId) ?? throw new AxoException("errors.notFound");
            var png = await _capes.Read(capeId, cancellationToken);
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
            using var _ = await _client.SendRaw(HttpMethod.Post, "/capes/submissions", form, cancellationToken);
            // Es gibt höchstens eine Einreichung: alle anderen gelten wieder als lokal
            foreach (var other in (await _capes.List(cancellationToken)).Where(capeView => capeView.Status == CapeStatus.Pending && capeView.Id != capeId))
            {
                await _capes.SetStatus(other.Id, CapeStatus.Local, null, cancellationToken);
            }

            await _capes.SetStatus(capeId, CapeStatus.Pending, null, cancellationToken);
        }

        public async Task Withdraw(string capeId, CancellationToken cancellationToken)
        {
            await _client.Send(HttpMethod.Delete, "/capes/submissions", cancellationToken: cancellationToken);
            await _capes.SetStatus(capeId, CapeStatus.Local, null, cancellationToken);
        }

        /// <summary>Ergebnis einer Freigabe aus einer Benachrichtigung übernehmen.</summary>
        public async Task ApplyReview(string localId, bool approved, string? reason, CancellationToken cancellationToken)
        {
            if (_textHelper.IsValidId(localId))
            {
                await _capes.SetStatus(localId, approved ? CapeStatus.Approved : CapeStatus.Rejected, reason, cancellationToken);
            }
        }

        // ---- Admin ----

        public async Task<IReadOnlyList<CapeSubmission>> Submissions(CancellationToken cancellationToken) =>
            await _client.Send<List<CapeSubmission>>(HttpMethod.Get, "/admin/submissions", cancellationToken: cancellationToken) ?? [];

        public Task Review(string submissionId, bool approve, string? reason, CancellationToken cancellationToken) =>
            _client.Send(HttpMethod.Post, $"/admin/submissions/{Uri.EscapeDataString(submissionId)}",
                new { approve, reason = reason == null ? null : _textHelper.Clean(reason, 200) }, cancellationToken);

        public async Task UploadOfficial(string name, byte[] png, CancellationToken cancellationToken)
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
            using var _ = await _client.SendRaw(HttpMethod.Post, "/admin/capes", form, cancellationToken);
        }

        public Task DeleteOfficial(string id, CancellationToken cancellationToken) =>
            _client.Send(HttpMethod.Delete, $"/admin/capes/{Uri.EscapeDataString(id)}", cancellationToken: cancellationToken);

        public async Task<IReadOnlyList<AdminEntry>> Admins(CancellationToken cancellationToken) =>
            await _client.Send<List<AdminEntry>>(HttpMethod.Get, "/admin/admins", cancellationToken: cancellationToken) ?? [];

        public Task SetAdmin(string uuid, bool admin, CancellationToken cancellationToken) =>
            _client.Send(admin ? HttpMethod.Put : HttpMethod.Delete, $"/admin/admins/{_uuidHelper.RequireUuid(uuid)}", cancellationToken: cancellationToken);
    }
}
