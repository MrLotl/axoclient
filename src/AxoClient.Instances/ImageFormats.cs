using AxoClient.Instances.Contract;

namespace AxoClient.Instances
{
    public class ImageFormats : IImageFormats
    {
        /// <summary>Größte erlaubte Bilddatei (Symbole, Banner).</summary>
        public int MaxImageBytes => 5 * 1024 * 1024;

        public bool IsPng(ReadOnlySpan<byte> data) =>
            data.Length > 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        public bool IsJpeg(ReadOnlySpan<byte> data) => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

        public bool IsPngOrJpeg(ReadOnlySpan<byte> data) => IsPng(data) || IsJpeg(data);

        /// <summary>Breite und Höhe aus dem PNG-Header (IHDR), ohne das Bild zu dekodieren.</summary>
        public (int Width, int Height)? PngSize(ReadOnlySpan<byte> data)
        {
            if (!IsPng(data) || data.Length < 24)
            {
                return null;
            }

            return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[16..20]),
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[20..24]));
        }

        public string MimeOf(ReadOnlySpan<byte> data) => IsPng(data) ? "image/png" : IsJpeg(data) ? "image/jpeg" : "application/octet-stream";
    }
}
