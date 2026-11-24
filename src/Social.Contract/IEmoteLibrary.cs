using AxoClient.DataModel.Social;

namespace AxoClient.Social.Contract
{
    public interface IEmoteLibrary
    {
        /// <summary>Eingebaute Emotes der Mod (Id und Name).</summary>
        IReadOnlyList<(string Id, string Name)> BuiltIn { get; }

        Task<IReadOnlyList<EmoteView>> List(CancellationToken ct = default);

        Task<IReadOnlyList<string?>> Slots(CancellationToken ct = default);

        Task SetSlots(IReadOnlyList<string?> slots, CancellationToken ct = default);

        /// <summary>Importiert eine Animationsdatei (Blockbench/GeckoLib-Bedrock oder PlayerAnimator/Emotecraft). Gibt die neuen Emotes zurück.</summary>
        Task<IReadOnlyList<EmoteView>> Import(string sourcePath, CancellationToken ct = default);

        Task Rename(string id, string name, CancellationToken ct = default);

        /// <summary>
        /// Stellt ein, ob das Emote dauerhaft läuft und ob man dabei laufen darf. "Dauerhaft" steht auch in der Animation selbst,
        /// damit andere Spieler es genauso sehen.
        /// </summary>
        Task SetOptions(string id, bool loop, bool moving, CancellationToken ct = default);

        Task Remove(string id, CancellationToken ct = default);

        /// <summary>Animationen der eingebauten Emotes (Bedrock-JSON aus der Mod, als Ressource eingebettet), oder <c>null</c>.</summary>
        string? BuiltInAnimations();

        /// <summary>Animation eines eigenen Emotes (kompaktes JSON) samt Name der Animation darin, oder <c>null</c>.</summary>
        Task<(string Json, string? Animation)?> Animation(string id, CancellationToken ct = default);

        /// <summary>Alle eigenen Emotes samt Animation – so bekommt sie die Mod.</summary>
        Task<IReadOnlyList<(EmoteItem Item, string Json)>> AllWithAnimations(CancellationToken ct = default);
    }
}
