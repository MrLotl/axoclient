using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record ContentListData(string Id, ContentType Type);

    private sealed record SearchData(string Source, ProjectKind Kind, string Text, string? Minecraft, LoaderType? Loader, string? Category,
        SearchSort Sort, int Page, int PageSize = 20);

    private sealed record ProjectData(string Source, string Id);

    private sealed record ProjectVersionsData(string Source, string ProjectId, string? InstanceId, ProjectKind Kind, bool AllVersions = false);

    private sealed record InstallData(string InstanceId, ContentType Type, string Source, string ProjectId, string? VersionId);

    private sealed record EnableData(string Id, ContentType Type, string File, bool Enabled);

    private sealed record RemoveData(string Id, ContentType Type, List<string> Files);

    private sealed record AddFilesData(string Id, ContentType Type, List<string> FileRefs);

    private sealed record VersionsForData(string Id, ContentType Type, string File, bool All = false);

    private sealed record ApplyUpdatesData(string Id, List<ContentUpdate> Updates);

    private sealed record ModpackInstallData(string Source, string ProjectId, string? VersionId);

    private sealed record ModpackUpdateData(string Id, string VersionId);

    private static void RegisterContent(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;

        r.On<ContentListData>("content.list", async (d, ct) => await host.Content.ListAsync(d.Id, d.Type, ct));

        r.On<SearchData>("content.search", async (d, ct) =>
        {
            await host.Settings.UpdateStateAsync(s =>
            {
                if (d.Kind == ProjectKind.Modpack)
                    s.ContentSource.Modpacks = d.Source;
                else
                    s.ContentSource.AddMods = d.Source;
            });
            return await host.Providers.Get(d.Source).SearchAsync(new SearchQuery(d.Kind, d.Text, d.Minecraft, d.Loader, d.Category, d.Sort,
                d.Page, d.PageSize), ct);
        });

        r.On<ProjectData>("content.project", async (d, ct) => await host.Providers.Get(d.Source).GetProjectAsync(d.Id, ct));

        r.On<ProjectVersionsData>("content.projectVersions", async (d, ct) =>
        {
            string? mc = null;
            LoaderType? loader = null;
            if (d.InstanceId != null && !d.AllVersions)
            {
                var instance = await host.Instances.GetAsync(d.InstanceId, ct);
                mc = instance.Minecraft.Version;
                loader = instance.Loader.Type;
            }
            return await host.Providers.Get(d.Source).GetVersionsAsync(d.ProjectId, mc, loader, d.Kind, ct);
        });

        r.On<InstallData>("content.install", (d, _) => Task.FromResult<object?>(StartJob(host.Jobs.RunAsync("content.install",
            new TextRef("content.installing"),
            job => host.Content.InstallAsync(d.InstanceId, d.Type, new InstallRequest(d.Source, d.ProjectId, d.VersionId), job), d.InstanceId))));

        r.On<EnableData>("content.setEnabled", async (d, ct) =>
        {
            await host.Content.SetEnabledAsync(d.Id, d.Type, d.File, d.Enabled, ct);
            return await host.Content.ListAsync(d.Id, d.Type, ct);
        });

        r.On<RemoveData>("content.remove", async (d, ct) =>
        {
            await host.Content.RemoveAsync(d.Id, d.Type, d.Files.Take(500).ToList(), ct);
            return await host.Content.ListAsync(d.Id, d.Type, ct);
        });

        r.On<AddFilesData>("content.addFiles", async (d, ct) =>
        {
            foreach (var fileRef in d.FileRefs.Take(200))
                await host.Content.AddLocalFileAsync(d.Id, d.Type, ctx.Files.Resolve(fileRef), ct);
            _ = host.Content.IdentifyLocalAsync(d.Id);
            return await host.Content.ListAsync(d.Id, d.Type, ct);
        });

        r.On<IdData>("content.identify", async (d, ct) =>
        {
            await host.Content.IdentifyLocalAsync(d.Id, ct);
            return null;
        });

        r.On<VersionsForData>("content.versionsFor", async (d, ct) => await host.Content.VersionsForAsync(d.Id, d.Type, d.File, d.All, ct));

        r.On<IdData>("content.updates", async (d, ct) => await host.Updates.ScanAsync(d.Id, ct));

        r.On<ApplyUpdatesData>("content.applyUpdates", (d, _) => Task.FromResult<object?>(StartJob(host.Jobs.RunAsync("content.update",
            new TextRef("content.updatingAll"), job => host.Updates.ApplyAsync(d.Id, d.Updates.Take(500).ToList(), job), d.Id))));

        r.On<IdData>("content.repair", (d, _) => Task.FromResult<object?>(StartJob(host.Jobs.RunAsync("content.repair",
            new TextRef("content.repairing"), job => host.CrashActions.RunAsync(d.Id, "repairInstance", null, job), d.Id))));

        // ---- Modpacks ----
        r.On<ProjectData>("modpacks.versions", async (d, ct) => await host.Modpacks.VersionsAsync(d.Source, d.Id, ct));

        r.On<ModpackInstallData>("modpacks.install", (d, _) => Task.FromResult<object?>(StartJob(host.Jobs.RunAsync("modpack.install",
            new TextRef("modpacks.installing"), job => host.Modpacks.InstallFromSourceAsync(d.Source, d.ProjectId, d.VersionId, job)))));

        r.On<ModpackUpdateData>("modpacks.update", (d, _) => Task.FromResult<object?>(StartJob(host.Jobs.RunAsync("modpack.update",
            new TextRef("modpacks.updating"), job => host.Modpacks.UpdateAsync(d.Id, d.VersionId, job), d.Id))));
    }
}
