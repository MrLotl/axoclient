namespace AxoClient.Instances.Contract
{
    public interface IImageFormats
    {
        bool IsPng(ReadOnlySpan<byte> data);

        bool IsJpeg(ReadOnlySpan<byte> data);

        bool IsPngOrJpeg(ReadOnlySpan<byte> data);

        /// <summary>Breite und Höhe aus dem PNG-Header (IHDR), ohne das Bild zu dekodieren.</summary>
        (int Width, int Height)? PngSize(ReadOnlySpan<byte> data);

        string MimeOf(ReadOnlySpan<byte> data);

        /// <summary>Größte erlaubte Bilddatei (Symbole, Banner).</summary>
        int MaxImageBytes { get; }
    }
}
