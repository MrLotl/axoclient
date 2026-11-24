using System.IO.Abstractions;

namespace AxoClient.Common
{
    /// <summary>Legt <see cref="JsonStore{T}"/> an und gibt ihnen die gemeinsamen Hilfsklassen mit.</summary>
    public class JsonStoreFactory : IJsonStoreFactory
    {
        private readonly IJsonHelper _jsonHelper;
        private readonly IAppLog _appLog;

        public JsonStoreFactory(IJsonHelper jsonHelper, IAppLog appLog)
        {
            _jsonHelper = jsonHelper;
            _appLog = appLog;
        }

        public JsonStore<T> Create<T>(IFileSystem fs, string path, int currentVersion = 1, IEnumerable<JsonMigration>? migrations = null)
            where T : class, IVersionedDocument, new()
        {
            return new JsonStore<T>(fs, path, _jsonHelper, _appLog, currentVersion, migrations);
        }
    }
}
