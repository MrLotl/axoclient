namespace AxoClient.DataModel.Skins
{
    public sealed class CapeLibraryDocument : ILibraryDocument<CapeItem>
    {
        public int FormatVersion { get; set; } = 1;
        public List<CapeItem> Capes { get; set; } = [];

        List<CapeItem> ILibraryDocument<CapeItem>.Items => Capes;
    }
}
