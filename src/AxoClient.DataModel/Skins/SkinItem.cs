namespace AxoClient.DataModel.Skins
{
    public sealed class SkinItem : ILibraryItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
        public SkinModel Model { get; set; } = SkinModel.Classic;
        public SkinOrigin Origin { get; set; } = new();
        public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
