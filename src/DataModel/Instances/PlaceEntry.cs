namespace AxoClient.DataModel.Instances
{
    /// <summary>Spielzeit an einem Ort: Einzelspieler-Welt (Ordnername) oder Server (Adresse).</summary>
    public sealed class PlaceEntry
    {
        /// <summary><c>world</c> oder <c>server</c>.</summary>
        public string Kind { get; set; } = "";
        public string Id { get; set; } = "";
        public long Seconds { get; set; }
        public DateTimeOffset? LastPlayed { get; set; }
    }
}
