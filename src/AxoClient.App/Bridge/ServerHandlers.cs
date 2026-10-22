using AxoClient.Core.Common;
using AxoClient.Core.Servers;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record SoftwareData(ServerSoftware Software);

    private sealed record ServerSaveData(string? Id, string Name, ServerSoftware Software, string Version, int Port, int MaxPlayers, int MemoryMb,
        bool OpenToInternet, bool EulaAccepted, string? ImportRef = null);

    private sealed record ServerImageData(string Id, bool Banner, string? Base64);

    private static void RegisterServers(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;

        r.On("localServers.list", async ct => await host.LocalServers.ListAsync(ct));
        r.On<SoftwareData>("localServers.versions", async (d, ct) => await host.LocalServers.VersionsAsync(d.Software, ct));
        r.On("localServers.nextPort", async ct => await host.LocalServers.NextFreePortAsync(ct));

        r.On<ServerSaveData>("localServers.save", async (d, ct) =>
        {
            var request = new ServerRequest(d.Name, d.Software, d.Version, d.Port, d.MaxPlayers, d.MemoryMb, d.OpenToInternet, d.EulaAccepted);
            if (d.Id == null && d.ImportRef != null)
            {
                var folder = ctx.Files.Resolve(d.ImportRef);
                return await host.Jobs.RunAsync("server.import", TextRef.Of("localServers.import.title", ("name", request.Name)),
                    job => host.LocalServers.ImportAsync(request, folder, job));
            }
            return d.Id == null
                ? await host.LocalServers.CreateAsync(request, ct)
                : await host.LocalServers.UpdateAsync(d.Id, request, ct);
        });

        // Vorhandenen Server-Ordner wählen und auswerten; übernommen wird erst beim Speichern im Server-Dialog
        r.On("localServers.inspectFolder", async _ =>
        {
            var folder = await ctx.Window.OpenFolderAsync("Server-Ordner wählen");
            if (folder == null)
                return null;
            var info = ServerImport.Inspect(folder);
            return new { folderRef = ctx.Files.Add(folder), info };
        });

        r.On<ServerImageData>("localServers.setImage", async (d, ct) =>
        {
            byte[]? bytes;
            try
            {
                bytes = d.Base64 == null ? null : Convert.FromBase64String(d.Base64);
            }
            catch (FormatException)
            {
                throw new AxoException("errors.invalidImage");
            }
            await host.LocalServers.SetImageAsync(d.Id, d.Banner, bytes, ct);
            return null;
        });

        r.On<IdData>("localServers.delete", async (d, ct) =>
        {
            await host.LocalServers.DeleteAsync(d.Id, ct);
            return null;
        });

        r.On<IdData>("localServers.start", (d, _) => Task.FromResult<object?>(StartJob(host.LocalServers.StartAsync(d.Id))));

        r.On<IdData>("localServers.stop", async (d, _) =>
        {
            await host.LocalServers.StopAsync(d.Id);
            return null;
        });

        r.On<CrashActionData>("localServers.crashAction", async (d, _) =>
        {
            await host.LocalServers.RunCrashActionAsync(d.Id, d.Action, d.File);
            return null;
        });
    }
}
