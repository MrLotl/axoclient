using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.DataModel.Servers;
using AxoClient.Servers.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich Server.</summary>
    public class ServerHandler : BridgeHandler
    {
        private readonly ILocalServerManager _localServers;
        private readonly IJobManager _jobs;
        private readonly FileRefs _files;
        private readonly IServerImport _serverImport;

        public ServerHandler(
            ILocalServerManager localServers,
            IJobManager jobs,
            FileRefs files,
            IServerImport serverImport,
            IAppLog appLog,
            ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _localServers = localServers;
            _jobs = jobs;
            _files = files;
            _serverImport = serverImport;
        }

        public override void Register(MessageRouter r, MainWindow window)
        {
            r.On("localServers.list", async ct => await _localServers.List(ct));
            r.On<SoftwareData>("localServers.versions", async (d, ct) => await _localServers.Versions(d.Software, ct));
            r.On("localServers.nextPort", async ct => await _localServers.NextFreePort(ct));

            r.On<ServerSaveData>("localServers.save", async (d, ct) =>
            {
                var request = new ServerRequest(d.Name, d.Software, d.Version, d.Port, d.MaxPlayers, d.MemoryMb, d.OpenToInternet, d.EulaAccepted);
                if (d.Id == null && d.ImportRef != null)
                {
                    var folder = _files.Resolve(d.ImportRef);
                    return await _jobs.Run("server.import", new TextRef("localServers.import.title", ("name", request.Name)),
                        job => _localServers.Import(request, folder, job));
                }
                return d.Id == null
                    ? await _localServers.Create(request, ct)
                    : await _localServers.Update(d.Id, request, ct);
            });

            // Vorhandenen Server-Ordner wählen und auswerten; übernommen wird erst beim Speichern im Server-Dialog
            r.On("localServers.inspectFolder", async _ =>
            {
                var folder = await window.OpenFolder("Server-Ordner wählen");
                if (folder == null)
                {
                    return null;
                }

                var info = _serverImport.Inspect(folder);
                return new { folderRef = _files.Add(folder), info };
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
                await _localServers.SetImage(d.Id, d.Banner, bytes, ct);
                return null;
            });

            r.On<IdData>("localServers.delete", async (d, ct) =>
            {
                await _localServers.Delete(d.Id, ct);
                return null;
            });

            r.On<IdData>("localServers.start", (d, _) => Task.FromResult<object?>(StartJob(_localServers.Start(d.Id))));

            r.On<IdData>("localServers.stop", async (d, _) =>
            {
                await _localServers.Stop(d.Id);
                return null;
            });

            r.On<CrashActionData>("localServers.crashAction", async (d, _) =>
            {
                await _localServers.RunCrashAction(d.Id, d.Action, d.File);
                return null;
            });
        }
    }
}
