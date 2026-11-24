namespace AxoClient.DataModel.Settings
{
    public sealed class ViewState
    {
        public string Instances { get; set; } = "grid";

        /// <summary>Größe der Kacheln bzw. Zeilen auf der Instanzen-Seite, 0 = klein bis 1 = groß</summary>
        public double InstancesSize { get; set; } = 0.5;
        public string LocalServers { get; set; } = "list";
        public string Modpacks { get; set; } = "grid";

        /// <summary>Mods, Ressourcepacks und Shader einer Instanz</summary>
        public string Content { get; set; } = "grid";

        /// <summary>Größe der Kacheln bzw. Zeilen im Inhalt-Tab, 0 = klein bis 1 = groß</summary>
        public double ContentGridSize { get; set; } = 0.5;

        public double ContentListSize { get; set; } = 0.5;
    }
}
