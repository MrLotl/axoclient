namespace AxoClient.DataModel.Settings
{
    public sealed class SortState
    {
        public string Modpacks { get; set; } = "popular";
        public string AddMods { get; set; } = "popular";

        /// <summary>Instanzen-Seite: recent, name, playtime, mods oder custom (eigene Reihenfolge)</summary>
        public string Instances { get; set; } = "recent";
    }
}
