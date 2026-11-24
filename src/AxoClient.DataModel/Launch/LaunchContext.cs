using CmlLib.Core;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Instances;
using AxoClient.DataModel.Settings;

namespace AxoClient.DataModel.Launch
{
    /// <summary>Alles, was die Prüfungen über einen Start wissen müssen.</summary>
    public sealed class LaunchContext
    {
        public required string LaunchId { get; init; }
        public required Instance Instance { get; set; }
        public required ContentDocument Content { get; set; }
        public required LauncherSettings Settings { get; init; }
        public required MinecraftPath GamePath { get; init; }
        public required string GameDir { get; init; }
        public QuickPlay? QuickPlay { get; init; }

        /// <summary>Nach der Kontoprüfung gesetzt.</summary>
        public GameSession? Session { get; set; }

        /// <summary>Kein Internet, alle Dateien vorhanden: offline starten.</summary>
        public bool Offline { get; set; }

        /// <summary>Versions-ID für CmlLib nach der Loader-Installation (z. B. "fabric-loader-0.16.10-1.21.4").</summary>
        public string? VersionName { get; set; }

        /// <summary>Muss die Version (oder der Loader) noch installiert werden?</summary>
        public bool NeedsInstall { get; set; }

        /// <summary>Eigener Java-Pfad (aus Einstellungen), <c>null</c> = automatisch.</summary>
        public string? JavaOverride { get; set; }

        public int MemoryMb => Instance.MemoryMb ?? Settings.Defaults.MemoryMb;

        /// <summary>Wurde die Instanz seit der letzten Kompatibilitätsprüfung verändert?</summary>
        public bool ContentChanged => Content.CheckedAt == null || Content.ChangedAt == null || Content.ChangedAt > Content.CheckedAt;

        /// <summary>Hinweise ohne Dialog (z. B. "AxoClient-Funktionen nicht verfügbar").</summary>
        public List<TextRef> Hints { get; } = [];

        /// <summary>Weitergabe von Zwischenergebnissen zwischen Prüfungen.</summary>
        public Dictionary<string, object> Bag { get; } = new();
    }
}
