namespace AxoClient.DataModel.Social
{
    /// <summary>Was ein Freund in einem Paket mitschicken darf. Nie Pfade, nie Java-Argumente.</summary>
    public sealed class InstanceRecipe
    {
        public string Name { get; set; } = "";
        public string Minecraft { get; set; } = "";
        public string Loader { get; set; } = "vanilla";
        public string? LoaderVersion { get; set; }
        public List<RecipeContent> Content { get; set; } = [];
        public List<RecipeServer> Servers { get; set; } = [];
        public string? Options { get; set; }

        /// <summary>Mod-Schutz des Absenders: <c>pack</c> oder <c>full</c> (fehlt = frei). Welche Mods zum Pack gehören, steht an den Inhalten.</summary>
        public string? ModLock { get; set; }
    }
}
