namespace AxoClient.DataModel.Social
{
    /// <summary>Ein importiertes Emote: Animation als JSON-Datei in <c>emotes/</c>.</summary>
    public sealed class EmoteItem
    {
        /// <summary><c>c_</c> und 12 Hex-Zeichen aus dem Hash der Animation – bei allen Spielern gleich.</summary>
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string File { get; set; } = "";

        /// <summary>Name der Animation in der Datei (Bedrock/GeckoLib); <c>null</c> = die erste (PlayerAnimator/Emotecraft).</summary>
        public string? Animation { get; set; }

        /// <summary>Läuft dauerhaft (Schleife oder letzte Pose halten), bis man schleicht oder sich bewegt.</summary>
        public bool Loop { get; set; }

        /// <summary>Man darf dabei laufen; nur Schleichen beendet es.</summary>
        public bool Moving { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
