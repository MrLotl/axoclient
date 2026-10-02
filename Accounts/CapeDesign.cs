using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.Accounts;

public sealed class CapeDesign
{
    public const double MinZoom = 0.3, MaxZoom = 5;
    public const int PreviewScale = 4;
    private const int MaxSourceWidth = 1024;
    private static readonly int[] UploadScales = [16, 8, 4];

    public static readonly Rect OuterFace = new(1, 1, 10, 16);
    private static readonly Rect InnerFace = new(12, 1, 10, 16);
    private static readonly Rect ElytraWing = new(34, 0, 12, 22);
    private const int ElytraMaskX = 22;

    private static readonly string[] ElytraMask =
    [
        ".........#########......",
        "..........##............",
        "............########....",
        "............#########...",
        ".............#########..",
        ".............#########..",
        ".............#########..",
        ".............##########.",
        ".............##########.",
        ".............##########.",
        ".............##########.",
        "#.............##########",
        "#.............##########",
        "#.............##########",
        "#.............##########",
        "#.............##########",
        "#..............#########",
        "#..............#########",
        "#..............#########",
        "#...............########",
        "#...............########",
        "#................#######"
    ];

    private CapeDesign(BitmapSource image)
    {
        Image = image;
        ImageColor = AverageColor(image);
        Background = ImageColor;
    }

    public static CapeDesign FromPng(byte[] bytes)
    {
        BitmapSource image = Images.Decode(bytes);
        if (image.PixelWidth > MaxSourceWidth)
            image = Images.Decode(bytes, MaxSourceWidth);
        return new CapeDesign(image);
    }

    public BitmapSource Image { get; }
    public Color ImageColor { get; }
    public Color Background { get; set; }
    public double Zoom { get; set; } = 1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public bool ImageInside { get; set; }
    public bool ImageOnElytra { get; set; } = true;
    public bool PixelArt { get; set; }

    public void Reset()
    {
        Zoom = 1;
        OffsetX = OffsetY = 0;
    }

    public BitmapSource RenderOuterFace(double pixelsPerUnit) =>
        Render((int)Math.Round(OuterFace.Width * pixelsPerUnit), (int)Math.Round(OuterFace.Height * pixelsPerUnit),
            pixelsPerUnit, dc =>
            {
                dc.PushTransform(new TranslateTransform(-OuterFace.X, -OuterFace.Y));
                FillBackground(dc);
                DrawOn(dc, OuterFace);
                dc.Pop();
            });

    public BitmapSource RenderTexture(int scale) =>
        CutOutElytra(Render(64 * scale, 32 * scale, scale, dc =>
        {
            FillBackground(dc);
            DrawOn(dc, OuterFace);
            if (ImageInside)
                DrawOn(dc, InnerFace);
            if (ImageOnElytra)
                DrawOn(dc, ElytraWing);
        }), scale);

    private static BitmapSource CutOutElytra(BitmapSource texture, int scale)
    {
        var stride = texture.PixelWidth * 4;
        var pixels = new byte[stride * texture.PixelHeight];
        texture.CopyPixels(pixels, stride, 0);
        for (var row = 0; row < ElytraMask.Length; row++)
        for (var column = 0; column < ElytraMask[row].Length; column++)
        {
            if (ElytraMask[row][column] == '#')
                continue;
            for (var y = row * scale; y < (row + 1) * scale; y++)
                Array.Clear(pixels, y * stride + (ElytraMaskX + column) * scale * 4, scale * 4);
        }
        var result = BitmapSource.Create(texture.PixelWidth, texture.PixelHeight, 96, 96, PixelFormats.Pbgra32, null,
            pixels, stride);
        result.Freeze();
        return result;
    }

    public byte[] ToPng(int scale) => Encode(RenderTexture(scale));

    public byte[] ToUploadPng()
    {
        byte[] png = [];
        foreach (var scale in UploadScales)
        {
            png = ToPng(scale);
            if (png.Length <= ClientCapeService.MaxUploadBytes)
                break;
        }
        return png;
    }

    private BitmapSource Render(int width, int height, double scale, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, PixelArt || Image.PixelWidth < 64
            ? BitmapScalingMode.NearestNeighbor
            : BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            draw(dc);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void FillBackground(DrawingContext dc) =>
        dc.DrawRectangle(new SolidColorBrush(Background), null, new Rect(0, 0, 64, 32));

    private void DrawOn(DrawingContext dc, Rect face)
    {
        var size = Math.Max(face.Width / Image.PixelWidth, face.Height / Image.PixelHeight) * Zoom;
        double width = Image.PixelWidth * size, height = Image.PixelHeight * size;
        var x = face.X + face.Width * (0.5 + OffsetX) - width / 2;
        var y = face.Y + face.Height * (0.5 + OffsetY) - height / 2;
        dc.PushClip(new RectangleGeometry(face));
        dc.DrawImage(Image, new Rect(x, y, width, height));
        dc.Pop();
    }

    private static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static Color AverageColor(BitmapSource image)
    {
        var (pixels, width, height) = Images.ReadBgra(image);
        var step = Math.Max(1, width * height / 20_000);
        double r = 0, g = 0, b = 0, weight = 0;
        for (var i = 0; i < width * height; i += step)
        {
            var alpha = pixels[i * 4 + 3] / 255.0;
            b += pixels[i * 4] * alpha;
            g += pixels[i * 4 + 1] * alpha;
            r += pixels[i * 4 + 2] * alpha;
            weight += alpha;
        }
        return weight < 1
            ? Color.FromRgb(0x30, 0x30, 0x30)
            : Color.FromRgb((byte)(r / weight), (byte)(g / weight), (byte)(b / weight));
    }

    public static byte[] PlainSkin()
    {
        var pixels = Enumerable.Repeat(new byte[] { 0x88, 0x88, 0x88, 0xFF }, 64 * 64).SelectMany(p => p).ToArray();
        return Encode(BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4));
    }
}
