using System.Net.Http.Headers;
using AxoClient.Core.Accounts;
using AxoClient.Core.Common;
using AxoClient.Core.Skins;

namespace AxoClient.Core.Social;

public sealed record OfficialCape(string Id, string Name, string Url);

public sealed record CapeSubmission(string Id, string OwnerUuid, string OwnerName, string Name, string Url, long Created);

public sealed record AdminEntry(string Uuid, string Name);

/// <summary>Axo-Capes beim Online-Service: offizielle Capes, eigenes aktives Cape, Einreichungen und Admin-Freigabe.</summary>
public sealed class CapeService(OnlineClient client, CapeManager capes, AccountManager accounts)
{
    public async Task<IReadOnlyList<OfficialCape>> OfficialAsync(CancellationToken ct) =>
        await client.SendAsync<List<OfficialCape>>(HttpMethod.Get, "/capes", ct: ct) ?? [];

    /// <summary>Axo-Cape setzen (offiziell oder eigenes freigegebenes) oder mit <c>null</c> ablegen.</summary>
    public async Task SetActiveAsync(AxoCapeChoice? choice, CancellationToken ct)
    {
        // Der Dienst antwortet mit der öffentlichen Bild-ID (bei eigenen Capes nicht die ID im Launcher)
        var result = await client.SendAsync<ActiveCapeResponse>(HttpMethod.Put, "/capes/active", new
        {
            kind = choice?.Source.ToString().ToLowerInvariant(),
            id = choice?.Id
        }, ct);
        if (accounts.Active is { } account)
        {
            await accounts.UpdateEntryAsync(account.Uuid, a => a.Capes.Axo = choice);
            ActiveChanged?.Invoke(account.Uuid, choice == null ? null : result?.Cape ?? choice.Id);
        }
    }

    private sealed record ActiveCapeResponse(string? Cape);

    /// <summary>Axo-Cape eines Kontos geändert: (UUID, öffentliche Bild-ID oder <c>null</c>).</summary>
    public event Action<string, string?>? ActiveChanged;

    /// <summary>Eigenes Cape zur Veröffentlichung einreichen (ersetzt eine frühere Einreichung).</summary>
    public async Task SubmitAsync(string capeId, CancellationToken ct)
    {
        var view = (await capes.ListAsync(ct)).FirstOrDefault(c => c.Id == capeId) ?? throw new AxoException("errors.notFound");
        var png = await capes.ReadAsync(capeId, ct);
        if (!SkinFiles.IsValidCape(png))
            throw new AxoException("capes.invalidImage");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(view.Name), "name");
        form.Add(new StringContent(capeId), "localId");
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "cape.png");
        using var _ = await client.SendRawAsync(HttpMethod.Post, "/capes/submissions", form, ct);
        // Es gibt höchstens eine Einreichung: alle anderen gelten wieder als lokal
        foreach (var other in (await capes.ListAsync(ct)).Where(c => c.Status == CapeStatus.Pending && c.Id != capeId))
            await capes.SetStatusAsync(other.Id, CapeStatus.Local, null, ct);
        await capes.SetStatusAsync(capeId, CapeStatus.Pending, null, ct);
    }

    public async Task WithdrawAsync(string capeId, CancellationToken ct)
    {
        await client.SendAsync(HttpMethod.Delete, "/capes/submissions", ct: ct);
        await capes.SetStatusAsync(capeId, CapeStatus.Local, null, ct);
    }

    /// <summary>Ergebnis einer Freigabe aus einer Benachrichtigung übernehmen.</summary>
    public async Task ApplyReviewAsync(string localId, bool approved, string? reason, CancellationToken ct)
    {
        if (Text.IsValidId(localId))
            await capes.SetStatusAsync(localId, approved ? CapeStatus.Approved : CapeStatus.Rejected, reason, ct);
    }

    // ---- Admin ----

    public async Task<IReadOnlyList<CapeSubmission>> SubmissionsAsync(CancellationToken ct) =>
        await client.SendAsync<List<CapeSubmission>>(HttpMethod.Get, "/admin/submissions", ct: ct) ?? [];

    public Task ReviewAsync(string submissionId, bool approve, string? reason, CancellationToken ct) =>
        client.SendAsync(HttpMethod.Post, $"/admin/submissions/{Uri.EscapeDataString(submissionId)}",
            new { approve, reason = reason == null ? null : Text.Clean(reason, 200) }, ct);

    public async Task UploadOfficialAsync(string name, byte[] png, CancellationToken ct)
    {
        if (!SkinFiles.IsValidCape(png))
            throw new AxoException("capes.invalidImage");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(Text.Clean(name, 40)), "name");
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "cape.png");
        using var _ = await client.SendRawAsync(HttpMethod.Post, "/admin/capes", form, ct);
    }

    public Task DeleteOfficialAsync(string id, CancellationToken ct) =>
        client.SendAsync(HttpMethod.Delete, $"/admin/capes/{Uri.EscapeDataString(id)}", ct: ct);

    public async Task<IReadOnlyList<AdminEntry>> AdminsAsync(CancellationToken ct) =>
        await client.SendAsync<List<AdminEntry>>(HttpMethod.Get, "/admin/admins", ct: ct) ?? [];

    public Task SetAdminAsync(string uuid, bool admin, CancellationToken ct) =>
        client.SendAsync(admin ? HttpMethod.Put : HttpMethod.Delete, $"/admin/admins/{SocialService.RequireUuid(uuid)}", ct: ct);
}
