namespace AxoClient.DataModel.Content
{
    /// <summary>Das Nötigste aus den Metadaten einer Mod-Jar für die Prüfungen vor dem Start.</summary>
    public sealed class ModMetadata
    {
        public string? ModId { get; set; }
        public List<string> Loaders { get; set; } = [];

        /// <summary>Versionsbedingung für Minecraft, roh (z. B. "~1.21.4" oder "[1.20.1,1.21)").</summary>
        public string? MinecraftRange { get; set; }
        public string? LoaderRange { get; set; }

        /// <summary>Pflicht-Abhängigkeiten als Mod-IDs (ohne minecraft/java/loader).</summary>
        public List<string> Depends { get; set; } = [];
        public List<string> Breaks { get; set; } = [];
        public List<string> Conflicts { get; set; } = [];

        /// <summary>
        /// Versionsbedingung je Mod-ID aus <see cref="Breaks"/>/<see cref="Conflicts"/> (fehlt = jede Version). Nur ab
        /// <see cref="Format"/> 2 vorhanden; ältere Einträge kennen die Bedingung nicht.
        /// </summary>
        public Dictionary<string, string> BreakRanges { get; set; } = [];
        public Dictionary<string, string> ConflictRanges { get; set; } = [];

        /// <summary>Version laut Metadaten der Jar (nicht der Name der Modrinth-Version).</summary>
        public string? ModVersion { get; set; }

        /// <summary>Stand des Lesers; ältere Einträge werden beim nächsten Abgleich neu gelesen.</summary>
        public int Format { get; set; }

        /// <summary>3: alle Mod-IDs einer Forge-Jar, reine Server-Abhängigkeiten ausgelassen. 4: Container-Jars (Kotlin for Forge). 5: alle Loader einer Jar. 6: auch Loader, bei denen sich die Jar nur über META-INF/services anmeldet.</summary>
        public const int CurrentFormat = 6;

        /// <summary>IDs, die diese Jar zusätzlich bereitstellt (<c>provides</c> und mitgelieferte Jars).</summary>
        public List<string> Provides { get; set; } = [];

        /// <summary>Java-Pakete in der Jar, um Absturzursachen einer Mod zuzuordnen.</summary>
        public List<string> Packages { get; set; } = [];

        /// <summary>Mixin-Konfigurationen (z. B. "sodium.mixins.json").</summary>
        public List<string> Mixins { get; set; } = [];
    }
}
