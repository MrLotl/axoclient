using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.Dialogs;

public static class ImageCropDialog
{
    private const double MaxFrameWidth = 680, MaxFrameHeight = 420;
    private const double MaxZoom = 6;

    public static async Task<string?> ShowAsync(AppServices app, string path, double aspect, int outputWidth, string title)
    {
        BitmapSource image;
        try
        {
            image = Images.Decode(File.ReadAllBytes(path));
            if (image.PixelWidth > 3000)
                image = Images.Decode(File.ReadAllBytes(path), 3000);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Bild zum Zuschneiden laden", ex);
            await app.Dialogs.ShowMessageAsync("Kein Bild", "Diese Datei kann nicht als Bild geöffnet werden.");
            return null;
        }

        var frameWidth = Math.Min(MaxFrameWidth, Math.Round(MaxFrameHeight * aspect));
        var frameHeight = Math.Round(frameWidth / aspect);
        double imageWidth = image.PixelWidth, imageHeight = image.PixelHeight;
        var baseScale = Math.Max(frameWidth / imageWidth, frameHeight / imageHeight);
        var zoom = 1.0;
        var scale = baseScale;
        double x = (frameWidth - imageWidth * scale) / 2, y = (frameHeight - imageHeight * scale) / 2;

        var picture = new Image { Source = image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        var canvas = new Canvas { Width = frameWidth, Height = frameHeight, Background = Ui.Resource<Brush>("DeepBg") };
        canvas.Children.Add(picture);
        var frame = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = frameWidth,
            Height = frameHeight,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Cursor = Cursors.SizeAll,
            Child = canvas,
            BorderBrush = Ui.Resource<Brush>("FieldBorder"),
            BorderThickness = new Thickness(1)
        };
        frame.Clip = new RectangleGeometry(new Rect(0, 0, frameWidth, frameHeight), 10, 10);

        var slider = new Slider { Minimum = 1, Maximum = MaxZoom, Value = 1, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var zoomText = new TextBlock { Width = 44, FontSize = 12.5, Foreground = Ui.Resource<Brush>("TextSecondary"), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };

        void Clamp()
        {
            double width = imageWidth * scale, height = imageHeight * scale;
            x = Math.Clamp(x, frameWidth - width, 0);
            y = Math.Clamp(y, frameHeight - height, 0);
        }

        void Apply()
        {
            Clamp();
            picture.Width = imageWidth * scale;
            picture.Height = imageHeight * scale;
            Canvas.SetLeft(picture, x);
            Canvas.SetTop(picture, y);
            zoomText.Text = $"{zoom * 100:0} %";
        }

        void ZoomTo(double value, Point anchor)
        {
            value = Math.Clamp(value, 1, MaxZoom);
            var next = baseScale * value;
            x = anchor.X - (anchor.X - x) * next / scale;
            y = anchor.Y - (anchor.Y - y) * next / scale;
            zoom = value;
            scale = next;
            Apply();
        }

        var updatingSlider = false;
        slider.ValueChanged += (_, e) =>
        {
            if (!updatingSlider)
                ZoomTo(e.NewValue, new Point(frameWidth / 2, frameHeight / 2));
        };
        frame.MouseWheel += (_, e) =>
        {
            ZoomTo(zoom * Math.Pow(1.12, e.Delta / 120.0), e.GetPosition(canvas));
            updatingSlider = true;
            slider.Value = zoom;
            updatingSlider = false;
            e.Handled = true;
        };

        Point? dragFrom = null;
        frame.MouseLeftButtonDown += (_, e) =>
        {
            dragFrom = e.GetPosition(canvas);
            frame.CaptureMouse();
            e.Handled = true;
        };
        frame.MouseMove += (_, e) =>
        {
            if (dragFrom is not { } from || !frame.IsMouseCaptured)
                return;
            var to = e.GetPosition(canvas);
            x += to.X - from.X;
            y += to.Y - from.Y;
            dragFrom = to;
            Apply();
        };
        frame.MouseLeftButtonUp += (_, _) =>
        {
            dragFrom = null;
            frame.ReleaseMouseCapture();
        };
        Apply();

        var zoomRow = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var minus = new Icon { Kind = "Minus", Size = 14, Foreground = Ui.Resource<Brush>("MutedText"), VerticalAlignment = VerticalAlignment.Center };
        var plus = new Icon { Kind = "Plus", Size = 14, Foreground = Ui.Resource<Brush>("MutedText"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(minus, Dock.Left);
        DockPanel.SetDock(zoomText, Dock.Right);
        DockPanel.SetDock(plus, Dock.Right);
        zoomRow.Children.Add(minus);
        zoomRow.Children.Add(zoomText);
        zoomRow.Children.Add(plus);
        zoomRow.Children.Add(slider);

        var hint = Ui.Note("Ziehen verschiebt das Bild, Mausrad oder Regler zoomen." +
            (aspect > 1.5 ? " Auf breiten Fenstern wird oben und unten etwas abgeschnitten." : ""), 12, 12.5);
        var content = Ui.Stack(hint, frame, zoomRow);

        if (!await app.Dialogs.ShowFormAsync(title, content, "Übernehmen", null, MaxFrameWidth + 48,
                "Wähle den Ausschnitt", "Image"))
            return null;

        try
        {
            var outputHeight = (int)Math.Round(outputWidth / aspect);
            var factor = outputWidth / frameWidth;
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
                dc.DrawImage(image, new Rect(x * factor, y * factor, imageWidth * scale * factor, imageHeight * scale * factor));
            var bitmap = new RenderTargetBitmap(outputWidth, outputHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var target = Path.Combine(Path.GetTempPath(), $"axoclient-crop-{Guid.NewGuid():N}.png");
            using (var stream = File.Create(target))
                encoder.Save(stream);
            return target;
        }
        catch (Exception ex)
        {
            await app.Dialogs.ShowErrorAsync("Bild konnte nicht zugeschnitten werden", ex);
            return null;
        }
    }
}
