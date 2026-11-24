using AxoClient.DataModel.Content;

namespace AxoClient.Content.Contract
{
    public interface IContentFolders
    {
        string Of(ContentType type);

        string ModrinthProjectType(ContentType type);
    }
}
