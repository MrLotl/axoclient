using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.Core;

public static class Images
{
    public static BitmapImage Decode(byte[] bytes, int decodeWidth = 0)
    {
        var image = new BitmapImage();
        using (var stream = new MemoryStream(bytes))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0)
                image.DecodePixelWidth = decodeWidth;
            image.StreamSource = stream;
            image.EndInit();
        }
        image.Freeze();
        return image;
    }

    public static BitmapSource? FromBytes(byte[]? bytes, int decodeWidth = 0)
    {
        if (bytes == null || bytes.Length == 0)
            return null;
        try
        {
            return Decode(bytes, decodeWidth);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Bild aus Rohdaten lesen", ex);
            return null;
        }
    }

    public static BitmapSource? FromFile(string path, int decodeWidth = 0)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0)
                image.DecodePixelWidth = decodeWidth;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"Bild \"{path}\" laden", ex);
            return null;
        }
    }

    public static (byte[] Pixels, int Width, int Height) ReadBgra(byte[] bytes) => ReadBgra(Decode(bytes));

    public static (byte[] Pixels, int Width, int Height) ReadBgra(BitmapSource image)
    {
        var source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return (pixels, source.PixelWidth, source.PixelHeight);
    }
}
