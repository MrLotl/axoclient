namespace AxoClient.DataModel.Content
{
    /// <summary>Inhalt von <c>modrinth.index.json</c> in einer <c>.mrpack</c>.</summary>
    public sealed class MrpackIndex
    {
        public int FormatVersion { get; set; } = 1;
        public string Game { get; set; } = "minecraft";
        public string VersionId { get; set; } = "1.0.0";
        public string Name { get; set; } = "";
        public string? Summary { get; set; }
        public List<MrpackFile> Files { get; set; } = [];
        public Dictionary<string, string> Dependencies { get; set; } = new();
    }
}
