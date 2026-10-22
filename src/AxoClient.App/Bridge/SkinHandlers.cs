using AxoClient.Core.Accounts;
using AxoClient.Core.Common;
using AxoClient.Core.Skins;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record SkinAddData(string? FileRef, string? Base64, string Name, SkinModel Model);

    private sealed record SkinAdoptData(string Player);

    private sealed record SkinUpdateData(string Id, string? Name, SkinModel? Model, string? Base64);

    private sealed record MojangCapeData(string? CapeId);

    private sealed record CapeSaveData(string? Id, string Name, string Base64, CapeSource Source, CapeEditState? Edit);

    private sealed record AxoCapeData(AxoCapeSource? Source, string? Id);

    private sealed record ReviewData(string Id, bool Approve, string? Reason);

    private sealed record OfficialCapeData(string Name, string Base64);

    private sealed record AdminData(string Uuid, bool Admin);

    private static void RegisterSkins(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;

        r.On("skins.list", async ct => await host.Skins.ListAsync(ct));

        r.On("skins.profile", async ct =>
        {
            var profile = await host.Mojang.GetOwnProfileAsync(ct);
            return profile;
        });

        r.On<SkinAddData>("skins.add", async (d, ct) =>
        {
            var bytes = d.FileRef != null ? await File.ReadAllBytesAsync(ctx.Files.Resolve(d.FileRef), ct)
                : d.Base64 != null ? Convert.FromBase64String(d.Base64) : throw new AxoException("errors.invalidInput");
            return await host.Skins.AddAsync(bytes, d.Name, d.Model, ct: ct);
        });

        r.On<SkinAdoptData>("skins.adopt", async (d, ct) => await host.Skins.AdoptFromPlayerAsync(d.Player, ct));

        r.On<SkinUpdateData>("skins.update", async (d, ct) =>
        {
            await host.Skins.UpdateAsync(d.Id, d.Name, d.Model, d.Base64 == null ? null : Convert.FromBase64String(d.Base64), ct);
            return await host.Skins.ListAsync(ct);
        });

        r.On<IdData>("skins.remove", async (d, ct) =>
        {
            await host.Skins.RemoveAsync(d.Id, ct);
            return await host.Skins.ListAsync(ct);
        });

        r.On<IdData>("skins.apply", async (d, ct) => await host.Skins.ApplyAsync(d.Id, ct));

        r.On<MojangCapeData>("skins.mojangCape", async (d, ct) => await host.Mojang.SetCapeAsync(d.CapeId, ct));

        // ---- Capes ----
        r.On("capes.list", async ct => await host.Capes.ListAsync(ct));

        r.On<CapeSaveData>("capes.save", async (d, ct) =>
            await host.Capes.SaveAsync(d.Id, d.Name, Convert.FromBase64String(d.Base64), d.Source, d.Edit, ct));

        r.On<IdData>("capes.editState", async (d, ct) => await host.Capes.LoadEditStateAsync(d.Id, ct));

        r.On<IdData>("capes.remove", async (d, ct) =>
        {
            await host.Capes.RemoveAsync(d.Id, ct);
            return await host.Capes.ListAsync(ct);
        });

        r.On("capes.official", async ct => await host.CapeService.OfficialAsync(ct));

        r.On<AxoCapeData>("capes.setActive", async (d, ct) =>
        {
            await host.CapeService.SetActiveAsync(d.Source is { } s && d.Id != null ? new AxoCapeChoice(s, d.Id) : null, ct);
            return null;
        });

        r.On<IdData>("capes.submit", async (d, ct) =>
        {
            await host.CapeService.SubmitAsync(d.Id, ct);
            return await host.Capes.ListAsync(ct);
        });

        r.On<IdData>("capes.withdraw", async (d, ct) =>
        {
            await host.CapeService.WithdrawAsync(d.Id, ct);
            return await host.Capes.ListAsync(ct);
        });

        // ---- Admin (Rollen prüft immer der Service, die UI blendet nur aus) ----
        r.On("admin.submissions", async ct => await host.CapeService.SubmissionsAsync(ct));
        r.On<ReviewData>("admin.review", async (d, ct) =>
        {
            await host.CapeService.ReviewAsync(d.Id, d.Approve, d.Reason, ct);
            return await host.CapeService.SubmissionsAsync(ct);
        });
        r.On<OfficialCapeData>("admin.uploadCape", async (d, ct) =>
        {
            await host.CapeService.UploadOfficialAsync(d.Name, Convert.FromBase64String(d.Base64), ct);
            return null;
        });
        r.On<IdData>("admin.deleteCape", async (d, ct) =>
        {
            await host.CapeService.DeleteOfficialAsync(d.Id, ct);
            return null;
        });
        r.On("admin.admins", async ct => await host.CapeService.AdminsAsync(ct));
        r.On<AdminData>("admin.setAdmin", async (d, ct) =>
        {
            await host.CapeService.SetAdminAsync(d.Uuid, d.Admin, ct);
            return await host.CapeService.AdminsAsync(ct);
        });
    }
}
