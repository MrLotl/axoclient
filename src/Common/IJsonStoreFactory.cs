using System.IO.Abstractions;

namespace AxoClient.Common
{
    public interface IJsonStoreFactory
    {
        JsonStore<T> Create<T>(IFileSystem fileSystem, string path, int currentVersion = 1, IEnumerable<JsonMigration>? migrations = null)
            where T : class, IVersionedDocument, new();
    }
}
