using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;

namespace AxoClient.Content
{
    public sealed class ContentProviders : IContentProviders
    {
        private readonly Dictionary<string, IContentProvider> _byName;

        public ContentProviders(IEnumerable<IContentProvider> providers)
        {
            _byName = providers.ToDictionary(p => p.Source);
        }

        public IReadOnlyCollection<IContentProvider> All => _byName.Values;

        public IContentProvider Get(string source) =>
            _byName.TryGetValue(source, out var p) && p.IsAvailable
                ? p
                : throw new AxoException("content.sourceUnavailable", new() { ["source"] = source });

        public IContentProvider Get(ContentSource source) => Get(source == ContentSource.CurseForge ? "curseforge" : "modrinth");

        public bool IsAvailable(string source) => _byName.TryGetValue(source, out var p) && p.IsAvailable;
    }
}
