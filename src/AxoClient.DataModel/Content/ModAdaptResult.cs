namespace AxoClient.DataModel.Content
{
    /// <summary>Ergebnis von <see cref="ContentManager.AdaptMods"/>.</summary>
    public sealed class ModAdaptResult
    {
        public int Updated { get; set; }
        public List<string> Disabled { get; } = [];
        public int Failed { get; set; }
    }
}
