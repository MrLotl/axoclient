using AxoClient.Common;

namespace AxoClient.DataModel.Content
{
    /// <summary>
    /// <c>instances/&lt;id&gt;/datapacks.json</c>: Herkunft der Datapacks je Welt. Die Dateien selbst liegen in der Welt,
    /// damit Minecraft sie findet; dort soll keine fremde Datei liegen.
    /// </summary>
    public sealed class DatapackDocument : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public Dictionary<string, List<ContentEntry>> Worlds { get; set; } = [];
    }
}
