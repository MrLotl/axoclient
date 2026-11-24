namespace AxoClient.DataModel.Instances
{
    public sealed class InstanceImages
    {
        public string? Icon { get; set; }
        public string? Banner { get; set; }
        public bool BannerAuto { get; set; } = true;

        /// <summary>Startwert für den Farbverlauf ohne Banner/Panorama; wird beim Erstellen einmal festgelegt.</summary>
        public string? ArtSeed { get; set; }

        /// <summary>Hintergrund ohne eigenes Bild: nur der Farbverlauf statt des Panoramas.</summary>
        public bool GradientOnly { get; set; }

        /// <summary>Blickrichtung im Panorama (0–3 = die vier Seiten um den Spieler).</summary>
        public int PanoramaView { get; set; }
    }
}
