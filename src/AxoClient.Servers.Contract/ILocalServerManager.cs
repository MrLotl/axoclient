using AxoClient.Common;
using AxoClient.DataModel.Servers;

namespace AxoClient.Servers.Contract
{
    public interface ILocalServerManager
    {
        string ProcessKey(string id);

        IServerSoftware Software(ServerSoftware type);

        Task<IReadOnlyList<ServerVersionInfo>> Versions(ServerSoftware type, CancellationToken ct);

        Task<IReadOnlyList<ServerView>> List(CancellationToken ct = default);

        Task<LocalServer> Get(string id, CancellationToken ct = default);

        string? IconPath(string id);

        string? BannerPath(string id);

        /// <summary>
        /// Servericon (<c>server-icon.png</c> im Serverordner, muss für Minecraft genau 64 × 64 PNG sein) oder Banner
        /// (nur für den Launcher). <paramref name="data"/> null entfernt das Bild.
        /// </summary>
        Task SetImage(string id, bool banner, byte[]? data, CancellationToken ct = default);

        Task<LocalServer> Create(ServerRequest request, CancellationToken ct = default);

        /// <summary>Neuen Server anlegen und den Inhalt eines vorhandenen Server-Ordners hineinkopieren.</summary>
        Task<LocalServer> Import(ServerRequest request, string folder, JobContext job);

        Task<LocalServer> Update(string id, ServerRequest request, CancellationToken ct = default);

        /// <summary>Nächster freier Port ab 25565 (für "Server erstellen").</summary>
        Task<int> NextFreePort(CancellationToken ct = default);

        Task Delete(string id, CancellationToken ct = default);

        Task Start(string id);

        Task Stop(string id);

        Task SendCommand(string id, string command);

        IReadOnlyList<LogLine> LogOf(string id, int max = 5000);

        /// <summary>Maßnahmen aus dem Server-Absturz-Dialog.</summary>
        Task RunCrashAction(string id, string action, string? file);

        bool AnyRunning { get; }
    }
}
