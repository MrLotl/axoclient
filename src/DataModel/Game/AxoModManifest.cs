namespace AxoClient.DataModel.Game
{
    public sealed class AxoModManifest
    {
        public int FormatVersion { get; set; } = 1;
        public List<AxoModEntry> Entries { get; set; } = [];
    }
}
