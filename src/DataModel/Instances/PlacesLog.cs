using AxoClient.Common;

namespace AxoClient.DataModel.Instances
{
    /// <summary><c>instances/&lt;id&gt;/places.json</c></summary>
    public sealed class PlacesLog : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public List<PlaceEntry> Places { get; set; } = [];
    }
}
