using System.Text.Json.Serialization;

namespace AxoClient.DataModel.Content
{
    /// <summary>Ein Eintrag in <c>content.json</c> (Mod, Ressourcepack oder Shader).</summary>
    public sealed class ContentEntry
    {
        public string File { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Version { get; set; }
        public bool Enabled { get; set; } = true;
        public ContentSource Source { get; set; } = ContentSource.Local;
        public string? ProjectId { get; set; }
        public string? VersionId { get; set; }
        public ContentHashes Hashes { get; set; } = new(null, null);
        public long Size { get; set; }
        public List<string> Dependencies { get; set; } = [];

        /// <summary>Mit welchem Stand des Lesers die Jar zuletzt gelesen wurde (auch wenn sie keine Metadaten hatte).</summary>
        public int MetaFormat { get; set; }
        public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Änderungszeit beim letzten Hashen: nur bei Änderung neu rechnen (Start-Pipeline).</summary>
        public DateTimeOffset? Modified { get; set; }

        /// <summary>Aus den Metadaten der Jar (fabric.mod.json, mods.toml …).</summary>
        public ModMetadata? Meta { get; set; }

        public string? IconUrl { get; set; }
        public string? Author { get; set; }

        /// <summary>
        /// Stand vor dem letzten Update bzw. Versionswechsel, um ihn zurückzuholen. Die Datei liegt in
        /// <c>instances/&lt;id&gt;/previous/&lt;ordner&gt;/</c>; es gibt immer nur eine Stufe.
        /// </summary>
        public ContentEntry? Previous { get; set; }

        [JsonIgnore]
        public string DiskName => Enabled ? File : File + ".disabled";
    }
}
