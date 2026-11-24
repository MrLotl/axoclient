namespace AxoClient.DataModel.Skins
{
    public sealed class SkinOrigin
    {
        public SkinOriginType Type { get; set; } = SkinOriginType.Upload;

        /// <summary>Bei "Von Spieler übernehmen": Name des Spielers.</summary>
        public string? Player { get; set; }
    }
}
