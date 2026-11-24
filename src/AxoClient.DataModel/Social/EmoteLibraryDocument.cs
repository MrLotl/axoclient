using AxoClient.Common;

namespace AxoClient.DataModel.Social
{
    public sealed class EmoteLibraryDocument : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public List<EmoteItem> Emotes { get; set; } = [];

        /// <summary>Belegung des Emote-Rads (8 Plätze, im Uhrzeigersinn ab oben): Emote-ID oder <c>null</c>.</summary>
        public List<string?>? Slots { get; set; }
    }
}
