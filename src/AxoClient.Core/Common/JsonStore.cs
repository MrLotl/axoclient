using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AxoClient.Core.Common;

/// <summary>Eine Migration hebt ein Dokument von <see cref="From"/> auf die nächste Formatversion.</summary>
public sealed record JsonMigration(int From, Action<JsonObject> Apply);

/// <summary>
/// Liest und schreibt genau eine JSON-Datei. Schreiben ist atomar (erst <c>.tmp</c>, dann umbenennen),
/// beim Lesen werden ältere Formatversionen schrittweise migriert. Eine kaputte Datei wird als
/// <c>.broken</c> beiseitegelegt, damit der Launcher trotzdem startet.
/// </summary>
public sealed class JsonStore<T> where T : class, IVersionedDocument, new()
{
    private readonly IFileSystem _fs;
    private readonly IReadOnlyList<JsonMigration> _migrations;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonStore(IFileSystem fs, string path, int currentVersion = 1, IEnumerable<JsonMigration>? migrations = null)
    {
        _fs = fs;
        Path = path;
        CurrentVersion = currentVersion;
        _migrations = (migrations ?? []).OrderBy(m => m.From).ToList();
    }

    public string Path { get; }
    public int CurrentVersion { get; }

    /// <summary>Wurde beim letzten Laden eine kaputte Datei beiseitegelegt?</summary>
    public bool RecoveredFromBrokenFile { get; private set; }

    public bool Exists => _fs.File.Exists(Path);

    public async Task<T> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await LoadUnlockedAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(T value, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await SaveUnlockedAsync(value, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Liest, ändert und schreibt unter einer Sperre, damit sich gleichzeitige Änderungen nicht überschreiben.</summary>
    public async Task<T> UpdateAsync(Action<T> change, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var value = await LoadUnlockedAsync(ct);
            change(value);
            await SaveUnlockedAsync(value, ct);
            return value;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<T> LoadUnlockedAsync(CancellationToken ct)
    {
        RecoveredFromBrokenFile = false;
        if (!_fs.File.Exists(Path))
            return new T { FormatVersion = CurrentVersion };

        string text;
        try
        {
            text = await _fs.File.ReadAllTextAsync(Path, ct);
        }
        catch (IOException)
        {
            // Datei kurz gesperrt (z. B. Virenscanner): einmal kurz warten und erneut lesen
            await Task.Delay(150, ct);
            text = await _fs.File.ReadAllTextAsync(Path, ct);
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
                throw new AxoException("errors.json.tooNew", new() { ["file"] = _fs.Path.GetFileName(Path) });

            foreach (var migration in _migrations.Where(m => m.From >= version && m.From < CurrentVersion))
            {
                migration.Apply(node);
                node["formatVersion"] = migration.From + 1;
            }
            node["formatVersion"] = CurrentVersion;

            return node.Deserialize<T>(Json.Options) ?? new T { FormatVersion = CurrentVersion };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            var broken = Path + ".broken";
            _fs.File.Copy(Path, broken, overwrite: true);
            RecoveredFromBrokenFile = true;
            AppLog.Warn($"{_fs.Path.GetFileName(Path)} war beschädigt und wurde nach .broken verschoben", ex);
            return new T { FormatVersion = CurrentVersion };
        }
    }

    private async Task SaveUnlockedAsync(T value, CancellationToken ct)
    {
        value.FormatVersion = CurrentVersion;
        var dir = _fs.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
            _fs.Directory.CreateDirectory(dir);

        var tmp = Path + ".tmp";
        await _fs.File.WriteAllTextAsync(tmp, Json.Serialize(value), ct);
        _fs.File.Move(tmp, Path, overwrite: true);
    }
}
