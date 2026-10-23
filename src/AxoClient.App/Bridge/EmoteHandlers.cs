using AxoClient.Core.Social;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record EmoteImportData(List<string> FileRefs);

    private sealed record EmoteRenameData(string Id, string Name);

    private sealed record EmoteSlotsData(List<string?> Slots);

    private sealed record EmoteOptionsData(string Id, bool Loop, bool Moving);

    private sealed record MissingModData(string Source, string ProjectId, string? VersionId);

    private sealed record InstallMissingData(string InstanceId, string Address, List<MissingModData> Mods);

    private static void RegisterEmotes(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;

        object Builtin() => EmoteLibrary.BuiltIn.Select(b => new { id = b.Id, name = b.Name, source = "axoclient" }).ToList();

        r.On("emotes.list", async ct => new
        {
            builtin = Builtin(),
            imported = await host.Emotes.ListAsync(ct),
            slots = await host.Emotes.SlotsAsync(ct)
        });
        r.On<EmoteImportData>("emotes.import", async (d, ct) =>
        {
            var added = new List<EmoteView>();
            foreach (var fileRef in d.FileRefs.Take(20))
                added.AddRange(await host.Emotes.ImportAsync(ctx.Files.Resolve(fileRef), ct));
            return added;
        });
        r.On<EmoteRenameData>("emotes.rename", async (d, ct) =>
        {
            await host.Emotes.RenameAsync(d.Id, d.Name, ct);
            return await host.Emotes.ListAsync(ct);
        });
        r.On<EmoteOptionsData>("emotes.setOptions", async (d, ct) =>
        {
            await host.Emotes.SetOptionsAsync(d.Id, d.Loop, d.Moving, ct);
            return await host.Emotes.ListAsync(ct);
        });
        r.On<IdData>("emotes.remove", async (d, ct) =>
        {
            await host.Emotes.RemoveAsync(d.Id, ct);
            return await host.Emotes.ListAsync(ct);
        });
        // Für die Vorschaubilder: Animation eines Emotes (eingebaut oder eigen) als JSON samt Name der Animation darin
        r.On<IdData>("emotes.animation", async (d, ct) =>
        {
            if (EmoteLibrary.BuiltIn.Any(b => b.Id == d.Id))
                return EmoteLibrary.BuiltInAnimations() is { } json ? new { json, animation = (string?)d.Id } : null;
            var own = await host.Emotes.AnimationAsync(d.Id, ct);
            return own is { } a ? new { json = a.Json, animation = a.Animation } : null;
        });
        r.On<EmoteSlotsData>("emotes.setSlots", async (d, ct) =>
        {
            await host.Emotes.SetSlotsAsync(d.Slots, ct);
            return await host.Emotes.SlotsAsync(ct);
        });

        // Aus dem Axo-Menü: fehlende Mods für die offene Welt eines Freundes installieren und neu starten
        r.On<InstallMissingData>("ingame.installMissing", (d, _) => Task.FromResult<object?>(StartJob(
            host.Ingame.InstallMissingAsync(d.InstanceId, d.Address, d.Mods.Select(m => (m.Source, m.ProjectId, m.VersionId)).ToList()))));
    }
}
