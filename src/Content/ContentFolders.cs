using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;

namespace AxoClient.Content
{
    public class ContentFolders : IContentFolders
    {
        public string Of(ContentType type) => type switch
        {
            ContentType.Mod => "mods",
            ContentType.ResourcePack => "resourcepacks",
            _ => "shaderpacks"
        };

        public string ModrinthProjectType(ContentType type) => type switch
        {
            ContentType.Mod => "mod",
            ContentType.ResourcePack => "resourcepack",
            _ => "shader"
        };
    }
}
