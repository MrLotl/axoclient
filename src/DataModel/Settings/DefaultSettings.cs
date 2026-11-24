namespace AxoClient.DataModel.Settings
{
    public sealed class DefaultSettings
    {
        public int MemoryMb { get; set; } = 6144;

        /// <summary><c>auto</c> oder ein Pfad zu javaw.exe.</summary>
        public string Java { get; set; } = "auto";
    }
}
