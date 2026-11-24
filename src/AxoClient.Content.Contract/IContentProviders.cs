using AxoClient.DataModel.Content;

namespace AxoClient.Content.Contract
{
    public interface IContentProviders
    {
        IReadOnlyCollection<IContentProvider> All { get; }

        IContentProvider Get(string source);

        IContentProvider Get(ContentSource source);

        bool IsAvailable(string source);
    }
}
