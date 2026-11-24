namespace AxoClient.DataModel.Instances
{
    /// <summary>Für die Übersicht: Ort mit Anzeigename und Bild.</summary>
    public sealed record PlaceView(string Kind, string Id, string Name, string? IconUrl, long Seconds, DateTimeOffset? LastPlayed, bool Exists);
}
