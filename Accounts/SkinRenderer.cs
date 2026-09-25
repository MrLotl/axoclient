using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.Accounts;

public static class SkinRenderer
{
    private const int OutW = 16, OutH = 32;

    public static BitmapSource RenderHead(byte[] png)
    {
        var head = new CroppedBitmap(RenderFront(png, false), new System.Windows.Int32Rect(4, 0, 8, 8));
        head.Freeze();
        return head;
    }

    public static BitmapSource? RenderCape(byte[] png)
    {
        try
        {
            var decoded = Images.Decode(png);
            var scale = Math.Max(1, decoded.PixelWidth / 64);
            var cape = new CroppedBitmap(decoded, new System.Windows.Int32Rect(scale, scale, 10 * scale, 16 * scale));
            cape.Freeze();
            return cape;
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Umhang-Bild zeichnen", ex);
            return null;
        }
    }

    public static BitmapSource RenderFront(byte[] png, bool slim)
    {
        var (pixels, w, h) = Images.ReadBgra(png);
        int scale = w / 64;

        bool legacy = h * 2 == w;
        int arm = slim ? 3 : 4;
        var output = new byte[OutW * OutH * 4];

        void Draw(int sx, int sy, int sw, int sh, int dx, int dy, bool overlay, bool mirror = false)
        {
            if (overlay && legacy && IsFullyOpaque(sx, sy, sw, sh))
                return;
            for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                int px = (sx + (mirror ? sw - 1 - x : x)) * scale;
                int py = (sy + y) * scale;
                int si = (py * w + px) * 4;
                int di = ((dy + y) * OutW + dx + x) * 4;
                byte a = pixels[si + 3];
                if (!overlay)
                {
                    output[di] = pixels[si];
                    output[di + 1] = pixels[si + 1];
                    output[di + 2] = pixels[si + 2];
                    output[di + 3] = 255;
                }
                else if (a > 0)
                {
                    for (int c = 0; c < 3; c++)
                        output[di + c] = (byte)((pixels[si + c] * a + output[di + c] * (255 - a)) / 255);
                    output[di + 3] = 255;
                }
            }
        }

        bool IsFullyOpaque(int sx, int sy, int sw, int sh)
        {
            for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
                if (pixels[(((sy + y) * scale) * w + (sx + x) * scale) * 4 + 3] != 255)
                    return false;
            return true;
        }

        Draw(8, 8, 8, 8, 4, 0, false);
        Draw(40, 8, 8, 8, 4, 0, true);
        Draw(20, 20, 8, 12, 4, 8, false);
        Draw(44, 20, arm, 12, 4 - arm, 8, false);
        Draw(4, 20, 4, 12, 4, 20, false);

        if (legacy)
        {
            Draw(44, 20, arm, 12, 12, 8, false, mirror: true);
            Draw(4, 20, 4, 12, 8, 20, false, mirror: true);
        }
        else
        {
            Draw(36, 52, arm, 12, 12, 8, false);
            Draw(20, 52, 4, 12, 8, 20, false);
            Draw(20, 36, 8, 12, 4, 8, true);
            Draw(44, 36, arm, 12, 4 - arm, 8, true);
            Draw(52, 52, arm, 12, 12, 8, true);
            Draw(4, 36, 4, 12, 4, 20, true);
            Draw(4, 52, 4, 12, 8, 20, true);
        }

        var result = BitmapSource.Create(OutW, OutH, 96, 96, PixelFormats.Bgra32, null, output, OutW * 4);
        result.Freeze();
        return result;
    }
}
