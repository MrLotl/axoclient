using System.Text.Json;
using AxoClient.DataModel.Social;

namespace AxoClient.Social.Contract
{
    public interface IInboxManager
    {
        Task SendInstance(string instanceId, IReadOnlyCollection<string> recipients, bool includeServers, bool includeOptions,
        bool includeModLock, CancellationToken ct);

        Task SendMod(string title, RecipeContent mod, IReadOnlyCollection<string> recipients, CancellationToken ct);

        Task SendServer(string name, string address, IReadOnlyCollection<string> recipients, CancellationToken ct);

        Task SendInvite(string address, string? instanceName, string? minecraft, IReadOnlyCollection<string> recipients,
        CancellationToken ct);

        Task<JsonElement> Fetch(string id, CancellationToken ct);

        Task Discard(string id, CancellationToken ct);

        /// <summary>Paket annehmen: Instanz anlegen, Mod installieren oder Server eintragen.</summary>
        Task<string?> Accept(string id, string? targetInstanceId, CancellationToken ct);
    }
}
