using AxoClient.Common;
using AxoClient.DataModel.Game;

namespace AxoClient.DataModel.Instances
{
    /// <summary><c>instances/&lt;id&gt;/instance.json</c></summary>
    public sealed class Instance : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public MinecraftVersionRef Minecraft { get; set; } = new("1.21.4", VersionType.Release);
        public LoaderRef Loader { get; set; } = new(LoaderType.Vanilla, null);

        /// <summary><c>null</c> = Standard aus den Launcher-Einstellungen.</summary>
        public int? MemoryMb { get; set; }

        /// <summary>Zusätzliche JVM-Argumente. Werden nie geteilt oder aus Importen übernommen.</summary>
        public string JavaArgs { get; set; } = "";
        public InstanceImages Images { get; set; } = new();
        public InstanceOrigin Origin { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>AxoClient-Mod in dieser Instanz verwenden (pro Instanz abschaltbar).</summary>
        public bool AxoMod { get; set; } = true;

        /// <summary>Mod-Schutz (frei, Pack geschützt, komplett gesperrt).</summary>
        public ModLock ModLock { get; set; } = new();
    }
}
