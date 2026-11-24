namespace AxoClient.Game.Contract
{
    public interface IPanoramaService
    {
        /// <summary>PNG einer Würfelseite (0 = Blick nach vorn), oder <c>null</c>, wenn es keine gibt.</summary>
        byte[]? Get(string version, int face = 0);

        /// <summary>
        /// Wie <see cref="Get"/>, lädt aber das Panorama nach, wenn die Version noch nie gestartet wurde.
        /// Ohne Internet oder bei unbekannten Versionen <c>null</c>.
        /// </summary>
        Task<byte[]?> Fetch(string version, int face = 0, CancellationToken ct = default);

        /// <summary>Versionsnamen wie „1.21.4“, „24w14a“ oder „1.20.1-pre2“; nichts, was aus dem Ordner führt.</summary>
        bool IsValidVersion(string version);
    }
}
