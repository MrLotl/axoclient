using System.Text.Json.Serialization;
using AxoClient.Common;
using AxoClient.DataModel.Content;

namespace AxoClient.DataModel.Instances
{
    public sealed class ModLock
    {
        public ModLockLevel Level { get; set; }

        /// <summary>Schlüssel der Mods, die beim Einschalten zum Pack gehörten (<see cref="KeyOf"/>).</summary>
        public List<string> PackMods { get; set; } = [];

        [JsonIgnore]
        public bool Active => Level != ModLockLevel.Off;

        /// <summary>Projekt-Id, wo bekannt (übersteht Updates), sonst der Dateiname ohne <c>.disabled</c>.</summary>
        public string KeyOf(ContentEntry entry) =>
            entry.ProjectId is { Length: > 0 } project ? "project:" + project : "file:" + entry.File.ToLowerInvariant();

        /// <summary>Gesperrt ist bei „Komplett“ jede Mod, bei „Pack“ nur, was beim Einschalten schon da war. Die AxoClient-Mod verwaltet der Launcher selbst.</summary>
        public bool IsLocked(ContentEntry entry) => entry.Source != ContentSource.AxoClient && Level switch
        {
            ModLockLevel.Full => true,
            ModLockLevel.Pack => PackMods.Contains(KeyOf(entry)) || PackMods.Contains("file:" + entry.File.ToLowerInvariant()),
            _ => false
        };

        public bool CanAdd() => Level != ModLockLevel.Full;

        public void ThrowIfLocked(ContentEntry entry)
        {
            if (IsLocked(entry))
            {
                throw new AxoException("content.modLock.locked", new() { ["name"] = entry.Name });
            }
        }

        public void ThrowIfCannotAdd()
        {
            if (!CanAdd())
            {
                throw new AxoException("content.modLock.full");
            }
        }

        /// <summary>
        /// Stellt die Stufe um. Beim Einschalten (oder mit <paramref name="adoptCurrent"/>) zählen alle Mods, die gerade
        /// in der Instanz liegen, zum Pack. Beim Wechsel zwischen „Pack“ und „Komplett“ bleibt die Liste, damit eigene Mods eigene bleiben.
        /// </summary>
        public void SetLevel(ModLockLevel level, IEnumerable<ContentEntry> currentMods, bool adoptCurrent = false)
        {
            if (level == ModLockLevel.Off)
            {
                PackMods = [];
            }
            else if (adoptCurrent || Level == ModLockLevel.Off || PackMods.Count == 0)
            {
                PackMods = currentMods.Where(m => m.Source != ContentSource.AxoClient).Select(KeyOf).Distinct().Order().ToList();
            }

            Level = level;
        }
    }
}
