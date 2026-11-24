using AxoClient.Common;

namespace AxoClient.DataModel.Settings
{
    /// <summary><c>state.json</c>: was sich der Launcher zwischen zwei Starts merkt (keine Einstellungen).</summary>
    public sealed class UiState : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public string? LastInstance { get; set; }
        public ViewState Views { get; set; } = new();
        public SortState Sort { get; set; } = new();
        public SourceState ContentSource { get; set; } = new();

        /// <summary>Als Favorit markierte Instanzen (stehen immer oben)</summary>
        public List<string> Favorites { get; set; } = [];

        /// <summary>Eigene Reihenfolge der Instanzen (IDs), gilt bei Sortierung „custom“</summary>
        public List<string> InstanceOrder { get; set; } = [];
    }
}
