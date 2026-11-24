namespace AxoClient.DataModel.Settings
{
    public sealed class AppearanceSettings
    {
        /// <summary><c>axo</c>, <c>mint</c>, <c>sunset</c>, <c>midnight</c> oder <c>custom:&lt;id&gt;</c> für einen eigenen Verlauf.</summary>
        public string Preset { get; set; } = "axo";
        public AccentGradient Accent { get; set; } = new("#db2777", "#7c3aed", 135);

        /// <summary>Eigene Verläufe, höchstens <see cref="Appearance.MaxCustomGradients"/>.</summary>
        public List<CustomGradient> CustomGradients { get; set; } = [];

        /// <summary>Skin auf der Startseite winkt einmal nach dem Start des Launchers.</summary>
        public bool WaveOnStart { get; set; } = true;

        /// <summary>Hintergrund der Startseite: Titelbild-Panorama der Minecraft-Version aus den Spieldateien.</summary>
        public bool VersionPanorama { get; set; } = true;

        /// <summary>Hintergrund bewegt sich langsam (Zoom).</summary>
        public bool StageMotion { get; set; } = true;
    }
}
