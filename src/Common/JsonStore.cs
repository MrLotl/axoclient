using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AxoClient.Common
{
    /// <summary>
    /// Liest und schreibt genau eine JSON-Datei. Schreiben ist atomar (erst <c>.tmp</c>, dann umbenennen),
    /// beim Lesen werden ältere Formatversionen schrittweise migriert. Eine kaputte Datei wird als
    /// <c>.broken</c> beiseitegelegt, damit der Launcher trotzdem startet.
    /// </summary>
    public sealed class JsonStore<T> where T : class, IVersionedDocument, new()
    {
        private readonly IFileSystem _fs;
        private readonly IJsonHelper _jsonHelper;
        private readonly IAppLog _appLog;
        private readonly IReadOnlyList<JsonMigration> _migrations;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public JsonStore(
            IFileSystem fileSystem,
            string path,
            IJsonHelper jsonHelper,
            IAppLog appLog,
            int currentVersion = 1,
            IEnumerable<JsonMigration>? migrations = null)
        {
            _fs = fileSystem;
            _jsonHelper = jsonHelper;
            _appLog = appLog;
            Path = path;
            CurrentVersion = currentVersion;
            _migrations = (migrations ?? []).OrderBy(jsonMigration => jsonMigration.From).ToList();
        }

        public string Path { get; }
        public int CurrentVersion { get; }

        /// <summary>Wurde beim letzten Laden eine kaputte Datei beiseitegelegt?</summary>
        public bool RecoveredFromBrokenFile { get; private set; }

        public bool Exists => _fs.File.Exists(Path);

        public async Task<T> Load(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                return await loadUnlocked(cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task Save(T value, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                await saveUnlocked(value, cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Liest, ändert und schreibt unter einer Sperre, damit sich gleichzeitige Änderungen nicht überschreiben.</summary>
        public async Task<T> Update(Action<T> change, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                var value = await loadUnlocked(cancellationToken);
                change(value);
                await saveUnlocked(value, cancellationToken);
                return value;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<T> loadUnlocked(CancellationToken cancellationToken)
        {
            RecoveredFromBrokenFile = false;
            if (!_fs.File.Exists(Path))
            {
                return new T { FormatVersion = CurrentVersion };
            }

            string text;
            try
            {
                text = await _fs.File.ReadAllTextAsync(Path, cancellationToken);
            }
            catch (IOException)
            {
                // Datei kurz gesperrt (z. B. Virenscanner): einmal kurz warten und erneut lesen
                await Task.Delay(150, cancellationToken);
                text = await _fs.File.ReadAllTextAsync(Path, cancellationToken);
            }

            try
            {
                var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) as JsonObject ?? throw new JsonException("Kein JSON-Objekt");

                var version = node["formatVersion"]?.GetValue<int>() ?? 1;
                if (version > CurrentVersion)
                {
                    throw new AxoException("errors.json.tooNew", new() { ["file"] = _fs.Path.GetFileName(Path) });
                }

                foreach (var migration in _migrations.Where(migration => migration.From >= version && migration.From < CurrentVersion))
                {
                    migration.Apply(node);
                    node["formatVersion"] = migration.From + 1;
                }
                node["formatVersion"] = CurrentVersion;

                return node.Deserialize<T>(_jsonHelper.Options) ?? new T { FormatVersion = CurrentVersion };
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
            {
                var broken = Path + ".broken";
                _fs.File.Copy(Path, broken, overwrite: true);
                RecoveredFromBrokenFile = true;
                _appLog.Warn($"{_fs.Path.GetFileName(Path)} war beschädigt und wurde nach .broken verschoben", exception);
                return new T { FormatVersion = CurrentVersion };
            }
        }

        private async Task saveUnlocked(T value, CancellationToken cancellationToken)
        {
            value.FormatVersion = CurrentVersion;
            var directory = _fs.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                _fs.Directory.CreateDirectory(directory);
            }

            var temporary = Path + ".tmp";
            await _fs.File.WriteAllTextAsync(temporary, _jsonHelper.Serialize(value), cancellationToken);
            _fs.File.Move(temporary, Path, overwrite: true);
        }
    }
}
