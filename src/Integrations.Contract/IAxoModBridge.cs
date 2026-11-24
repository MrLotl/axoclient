using System.Text.Json;
using AxoClient.Launch.Contract;

namespace AxoClient.Integrations.Contract
{
    public interface IAxoModBridge : IGameLaunchHook, IAsyncDisposable
    {
        /// <summary>
        /// Ob es ein Overlay-Fenster gibt, das die "hud"-Nachrichten der Mod (private Widgets) zeichnet. Setzt die App beim
        /// Start; nur dann bietet der Launcher "privateOverlay" an.
        /// </summary>
        bool PrivateOverlay { get; set; }

        /// <summary>Nachricht von der Mod: (Instanz-ID, Typ, Daten).</summary>
        event Action<string, string, JsonElement>? MessageReceived;

        int Port { get; }

        void Start();

        /// <summary>Nachricht an die Mod einer Instanz (oder aller verbundenen Mods).</summary>
        Task Send(string? instanceId, string type, object? data);

        /// <summary>Instanzen, deren Mod gerade verbunden ist.</summary>
        IReadOnlyList<string> ConnectedInstances { get; }

        bool IsConnected(string instanceId);
    }
}
