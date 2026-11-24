namespace AxoClient.DataModel.Content
{
    public sealed class MrpackFile
    {
        public string Path { get; set; } = "";
        public Dictionary<string, string> Hashes { get; set; } = new();
        public Dictionary<string, string>? Env { get; set; }
        public List<string> Downloads { get; set; } = [];
        public long FileSize { get; set; }
    }
}
