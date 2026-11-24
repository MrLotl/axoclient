using System.Text.Json;
using AxoClient.DataModel.Social;

namespace AxoClient.Social.Contract
{
    public interface ILiveChannel : IAsyncDisposable
    {
        /// <summary>
        /// Ein anderer Spieler im selben Raum spielt ein Emote (UUID ohne Bindestriche, Emote-ID oder null = Abbruch). Bei eigenen
        /// Emotes (<c>c_…</c>) kommt die Animation (JSON-Text) und ihr Name darin mit.
        /// </summary>
        event Action<RemoteEmote>? EmoteReceived;

        /// <summary>Gastgeber einer offenen Welt im selben Raum: Version, Loader und Mods (für "fehlende Mods").</summary>
        event Action<string, JsonElement>? WorldReceived;

        /// <summary>Server, auf dem gerade gespielt wird (oder null): wechselt bei Bedarf den Raum.</summary>
        void SetServer(string? address);

        /// <summary>
        /// Eigenes Emote an alle anderen im Raum (null = abbrechen). Eigene Emotes (<c>c_…</c>) schicken ihre Animation mit, damit
        /// andere sie ohne Import sehen. Ohne Verbindung passiert nichts.
        /// </summary>
        Task SendEmote(string? emote, string? anim = null, string? animation = null);

        /// <summary>Eigene offene Welt bekannt machen (Version, Loader, Mods) oder mit <c>null</c> zurückziehen.</summary>
        Task SendWorld(object? info);

        /// <summary>
        /// Schaut kurz in den Raum einer Adresse (ohne selbst etwas zu senden) und liefert die Welt-Infos des Gastgebers, falls
        /// dort jemand eine offene Welt bekannt gemacht hat.
        /// </summary>
        Task<JsonElement?> PeekWorld(string address, TimeSpan wait, CancellationToken ct);
    }
}
