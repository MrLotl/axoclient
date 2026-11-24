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

        public override void Register(MessageRouter router, MainWindow window)
        {
            router.On("localServers.list", async cancellationToken => await _localServers.List(cancellationToken));
            router.On<SoftwareData>("localServers.versions", async (request, cancellationToken) => await _localServers.Versions(request.Software, cancellationToken));
            router.On("localServers.nextPort", async cancellationToken => await _localServers.NextFreePort(cancellationToken));

            router.On<ServerSaveData>("localServers.save", async (saveRequest, cancellationToken) =>
            {
                var request = new ServerRequest(saveRequest.Name, saveRequest.Software, saveRequest.Version, saveRequest.Port, saveRequest.MaxPlayers, saveRequest.MemoryMb, saveRequest.OpenToInternet, saveRequest.EulaAccepted);
                if (saveRequest.Id == null && saveRequest.ImportRef != null)
                {
                    var folder = _files.Resolve(saveRequest.ImportRef);
                    return await _jobs.Run("server.import", new TextRef("localServers.import.title", ("name", request.Name)),
                        job => _localServers.Import(request, folder, job));
                }
                return saveRequest.Id == null
                    ? await _localServers.Create(request, cancellationToken)
                    : await _localServers.Update(saveRequest.Id, request, cancellationToken);
            });

            // Vorhandenen Server-Ordner wählen und auswerten; übernommen wird erst beim Speichern im Server-Dialog
            router.On("localServers.inspectFolder", async _ =>
            {
                var folder = await window.OpenFolder("Server-Ordner wählen");
                if (folder == null)
                {
                    return null;
                }

                var serverImportInfo = _serverImport.Inspect(folder);
                return new { folderRef = _files.Add(folder), info = serverImportInfo };
            });

            router.On<ServerImageData>("localServers.setImage", async (request, cancellationToken) =>
            {
                byte[]? bytes;
                try
                {
                    bytes = request.Base64 == null ? null : Convert.FromBase64String(request.Base64);
                }
                catch (FormatException)
                {
                    throw new AxoException("errors.invalidImage");
                }
                await _localServers.SetImage(request.Id, request.Banner, bytes, cancellationToken);
                return null;
            });

            router.On<IdData>("localServers.delete", async (request, cancellationToken) =>
            {
                await _localServers.Delete(request.Id, cancellationToken);
                return null;
            });

            router.On<IdData>("localServers.start", (request, _) => Task.FromResult<object?>(StartJob(_localServers.Start(request.Id))));

            router.On<IdData>("localServers.stop", async (request, _) =>
            {
                await _localServers.Stop(request.Id);
                return null;
            });

            router.On<CrashActionData>("localServers.crashAction", async (request, _) =>
            {
                await _localServers.RunCrashAction(request.Id, request.Action, request.File);
                return null;
            });
        }
    }
}
