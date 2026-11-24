namespace AxoClient.DataModel.Skins
{
    public sealed class SkinLibraryDocument : ILibraryDocument<SkinItem>
    {
        public int FormatVersion { get; set; } = 1;
        public List<SkinItem> Skins { get; set; } = [];

        List<SkinItem> ILibraryDocument<SkinItem>.Items => Skins;
    }
}
