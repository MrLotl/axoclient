using AxoClient.Common;

namespace AxoClient.DataModel.Content
{
    /// <summary><c>instances/&lt;id&gt;/content.json</c></summary>
    public sealed class ContentDocument : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public List<ContentEntry> Mods { get; set; } = [];
        public List<ContentEntry> Resourcepacks { get; set; } = [];
        public List<ContentEntry> Shaderpacks { get; set; } = [];
        public List<string> IgnoredWarnings { get; set; } = [];

        /// <summary>Wann sich Inhalte zuletzt geändert haben (Kompatibilität nur dann neu prüfen).</summary>
        public DateTimeOffset? ChangedAt { get; set; }
        public DateTimeOffset? CheckedAt { get; set; }

        public List<ContentEntry> ListOf(ContentType type) => type switch
        {
            ContentType.Mod => Mods,
            ContentType.ResourcePack => Resourcepacks,
            _ => Shaderpacks
        };
    }
}
