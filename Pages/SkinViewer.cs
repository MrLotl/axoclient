using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace McLauncher.Pages;

/// <summary>
/// 3D-Ansicht einer Minecraft-Figur, die sich mit der Maus drehen lässt (Ziehen; Doppelklick setzt zurück).
/// Die Skin-Textur wird auf sechs Quader (Kopf, Körper, Arme, Beine) samt zweiter Ebene gelegt, optional mit Umhang.
/// </summary>
public class SkinViewer : Grid
{
    private const double DefaultYaw = -28, DefaultPitch = 8;

    private readonly Viewport3D _viewport = new() { ClipToBounds = false, IsHitTestVisible = false };
    private readonly Model3DGroup _figure = new();
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), DefaultYaw);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), DefaultPitch);
    private Point _last;
    private bool _dragging;

    public SkinViewer()
    {
        Background = Brushes.Transparent; // damit auch leere Flächen die Maus annehmen
        Cursor = Cursors.SizeWE;
        ToolTip = "Ziehen zum Drehen, Doppelklick setzt die Ansicht zurück";

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(168, 168, 168)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(96, 96, 96), new Vector3D(-0.35, -0.5, -1)));

        _figure.Transform = new Transform3DGroup
        {
            Children =
            {
                new RotateTransform3D(_yaw, 0, 16, 0),
                new RotateTransform3D(_pitch, 0, 16, 0)
            }
        };

        var root = new Model3DGroup();
        root.Children.Add(lights);
        root.Children.Add(_figure);
        _viewport.Children.Add(new ModelVisual3D { Content = root });
        _viewport.Camera = new PerspectiveCamera(new Point3D(0, 17, 52), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 38);
        Children.Add(_viewport);

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                _yaw.Angle = DefaultYaw;
                _pitch.Angle = DefaultPitch;
                return;
            }
            _dragging = true;
            _last = e.GetPosition(this);
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (!_dragging)
                return;
            var pos = e.GetPosition(this);
            _yaw.Angle += (pos.X - _last.X) * 0.8;
            _pitch.Angle = Math.Clamp(_pitch.Angle + (pos.Y - _last.Y) * 0.4, -35, 35);
            _last = pos;
        };
        MouseLeftButtonUp += (_, _) =>
        {
            _dragging = false;
            ReleaseMouseCapture();
        };
    }

    /// <summary>Setzt Skin (PNG) und optional den Umhang (PNG). Ohne Skin bleibt die Ansicht leer.</summary>
    public void SetSkin(byte[]? png, bool slim, byte[]? capePng = null)
    {
        _figure.Children.Clear();
        if (png == null)
            return;

        byte[] skin;
        try
        {
            skin = ReadSkin(png);
        }
        catch
        {
            return;
        }

        // Jeder Skin-Pixel wird eine eigene einfarbige Fläche; gleiche Farben teilen sich ein Mesh.
        // (Texturen verzerrt WPF in 3D, und durchsichtige Stellen würden dahinterliegende Teile verdecken.)
        var meshes = new Dictionary<uint, MeshGeometry3D>();
        var arm = slim ? 3 : 4;

        void Box(int u, int v, int w, int h, int d, double x, double y, double z, double inflate, bool overlay) =>
            AddBox(skin, meshes, u, v, w, h, d, x, y, z, inflate, overlay);

        Box(0, 0, 8, 8, 8, -4, 24, -4, 0, false);              // Kopf
        Box(16, 16, 8, 12, 4, -4, 12, -2, 0, false);           // Körper
        Box(40, 16, arm, 12, 4, -4 - arm, 12, -2, 0, false);   // rechter Arm
        Box(32, 48, arm, 12, 4, 4, 12, -2, 0, false);          // linker Arm
        Box(0, 16, 4, 12, 4, -4, 0, -2, 0, false);             // rechtes Bein
        Box(16, 48, 4, 12, 4, 0, 0, -2, 0, false);             // linkes Bein

        Box(32, 0, 8, 8, 8, -4, 24, -4, 0.5, true);            // Hut
        Box(16, 32, 8, 12, 4, -4, 12, -2, 0.25, true);         // Jacke
        Box(40, 32, arm, 12, 4, -4 - arm, 12, -2, 0.25, true); // rechter Ärmel
        Box(48, 48, arm, 12, 4, 4, 12, -2, 0.25, true);        // linker Ärmel
        Box(0, 32, 4, 12, 4, -4, 0, -2, 0.25, true);           // rechte Hose
        Box(0, 48, 4, 12, 4, 0, 0, -2, 0.25, true);            // linke Hose

        AddMeshes(_figure, meshes);

        if (capePng != null)
            AddCape(capePng);
    }

    /// <summary>
    /// Umhang hinter dem Rücken: 10x16x1 Pixel, oben an den Schultern befestigt und leicht nach hinten geneigt.
    /// Wie in Minecraft ist er um 180° gedreht, damit die Außenseite (Textur ab 1,1) nach hinten zeigt.
    /// </summary>
    private void AddCape(byte[] capePng)
    {
        byte[] texture;
        int scale;
        try
        {
            texture = ReadCapeTexture(capePng, out scale);
        }
        catch
        {
            return;
        }
        var meshes = new Dictionary<uint, MeshGeometry3D>();
        AddBox(texture, meshes, 0, 0, 10, 16, 1, -5, -16, -0.5, 0, false, scale);

        var cape = new Model3DGroup
        {
            Transform = new Transform3DGroup
            {
                Children =
                {
                    new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 180)),
                    new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 8)), // unten etwas abstehend
                    new TranslateTransform3D(0, 24, -2.85) // Oberkante an den Schultern, hinter der Jacke
                }
            }
        };
        AddMeshes(cape, meshes);
        _figure.Children.Add(cape);
    }

    /// <summary>Ein Modell je Farbe; die Rückseite bekommt dieselbe Farbe.</summary>
    private static void AddMeshes(Model3DGroup group, Dictionary<uint, MeshGeometry3D> meshes)
    {
        foreach (var (argb, mesh) in meshes)
        {
            mesh.Freeze();
            var brush = new SolidColorBrush(Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            brush.Freeze();
            var material = new DiffuseMaterial(brush);
            material.Freeze();
            group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }
    }

    /// <summary>
    /// Fügt einen Quader hinzu. (u, v) ist die Ecke seines Bereichs in der Skin, (w, h, d) die Größe in Pixeln,
    /// (x, y, z) die untere linke hintere Ecke; <paramref name="inflate"/> vergrößert ihn nach allen Seiten.
    /// </summary>
    private static void AddBox(byte[] skin, Dictionary<uint, MeshGeometry3D> meshes, int u, int v, int w, int h, int d,
                               double x, double y, double z, double inflate, bool overlay, int scale = 1)
    {
        double x0 = x - inflate, x1 = x + w + inflate;
        double y0 = y - inflate, y1 = y + h + inflate;
        double z0 = z - inflate, z1 = z + d + inflate;

        // Eine Seite: tl/tr/bl sind die 3D-Ecken oben links, oben rechts, unten links (so wie in der Skin zu sehen)
        void Face(Point3D tl, Point3D tr, Point3D bl, int fu, int fv, int fw, int fh)
        {
            // scale: Pixel je Textur-Einheit (HD-Umhänge werden in voller Auflösung gezeigt)
            Vector3D right = (tr - tl) / (fw * scale), down = (bl - tl) / (fh * scale);
            for (var py = 0; py < fh * scale; py++)
            for (var px = 0; px < fw * scale; px++)
            {
                var i = ((fv * scale + py) * 64 * scale + fu * scale + px) * 4;
                var alpha = skin[i + 3];
                if (overlay && alpha < 128)
                    continue; // zweite Ebene: durchsichtige Pixel weglassen (die Grundebene ist immer deckend)
                var argb = (uint)(skin[i + 2] << 16 | skin[i + 1] << 8 | skin[i]);
                if (scale > 1)
                    argb &= 0xF8F8F8; // HD: ähnliche Farben zusammenfassen, sonst entstehen tausende Einzelmodelle
                if (!meshes.TryGetValue(argb, out var mesh))
                    meshes[argb] = mesh = new MeshGeometry3D();

                var p = tl + right * px + down * py;
                var n = mesh.Positions.Count;
                mesh.Positions.Add(p);
                mesh.Positions.Add(p + right);
                mesh.Positions.Add(p + right + down);
                mesh.Positions.Add(p + down);
                // Gegen den Uhrzeigersinn von außen gesehen (Vorderseite zeigt nach außen)
                foreach (var k in new[] { 0, 3, 2, 0, 2, 1 })
                    mesh.TriangleIndices.Add(n + k);
            }
        }

        Face(new(x0, y1, z1), new(x1, y1, z1), new(x0, y0, z1), u + d, v + d, w, h);             // vorne
        Face(new(x1, y1, z0), new(x0, y1, z0), new(x1, y0, z0), u + 2 * d + w, v + d, w, h);     // hinten
        Face(new(x0, y1, z0), new(x0, y1, z1), new(x0, y0, z0), u, v + d, d, h);                 // rechte Seite (-X)
        Face(new(x1, y1, z1), new(x1, y1, z0), new(x1, y0, z1), u + d + w, v + d, d, h);         // linke Seite (+X)
        Face(new(x0, y1, z0), new(x1, y1, z0), new(x0, y1, z1), u + d, v, w, d);                 // oben
        Face(new(x0, y0, z1), new(x1, y0, z1), new(x0, y0, z0), u + d + w, v, w, d);             // unten
    }

    /// <summary>
    /// Liest die Skin als 64x64 BGRA-Pixel: alte 64x32-Skins werden erweitert (linke Gliedmaßen gespiegelt),
    /// HD-Skins auf 64 heruntergerechnet.
    /// </summary>
    private static byte[] ReadSkin(byte[] png)
    {
        var skin = ReadTexture(png, out var legacy);
        if (legacy)
        {
            MirrorLimb(skin, 0, 16, 16, 48); // Bein
            MirrorLimb(skin, 40, 16, 32, 48); // Arm
        }
        return skin;
    }

    /// <summary>
    /// Umhang in voller Auflösung: Zeilenbreite 64 * <paramref name="scale"/> Pixel (128x64-Umhang: scale 2 usw.),
    /// damit auch feine Motive wie ein Logo scharf bleiben.
    /// </summary>
    private static byte[] ReadCapeTexture(byte[] png, out int scale)
    {
        var decoded = new BitmapImage();
        using (var ms = new MemoryStream(png))
        {
            decoded.BeginInit();
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.StreamSource = ms;
            decoded.EndInit();
        }
        var src = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        int sw = src.PixelWidth, sh = src.PixelHeight;
        var raw = new byte[sw * sh * 4];
        src.CopyPixels(raw, sw * 4, 0);

        scale = Math.Clamp(sw / 64, 1, 8); // mehr als 8-fach bringt in der kleinen Ansicht nichts
        var step = Math.Max(1, sw / (64 * scale));
        int width = 64 * scale, height = 32 * scale;
        var texture = new byte[width * height * 4];
        for (var y = 0; y < height && y * step < sh; y++)
        for (var x = 0; x < width && x * step < sw; x++)
            Array.Copy(raw, ((y * step) * sw + x * step) * 4, texture, (y * width + x) * 4, 4);
        return texture;
    }

    /// <summary>
    /// Liest eine Skin- oder Umhang-Textur als 64x64 BGRA-Pixel (bei HD-Texturen jeder n-te Pixel).
    /// <paramref name="halfHeight"/>: Die Textur ist nur halb so hoch wie breit (alter Skin bzw. Umhang).
    /// </summary>
    private static byte[] ReadTexture(byte[] png, out bool halfHeight)
    {
        var decoded = new BitmapImage();
        using (var ms = new MemoryStream(png))
        {
            decoded.BeginInit();
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.StreamSource = ms;
            decoded.EndInit();
        }
        var src = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        int sw = src.PixelWidth, sh = src.PixelHeight;
        var raw = new byte[sw * sh * 4];
        src.CopyPixels(raw, sw * 4, 0);

        var scale = Math.Max(1, sw / 64);
        halfHeight = sh * 2 == sw;
        int rows = Math.Min(64, sh / scale), cols = Math.Min(64, sw / scale);

        var texture = new byte[64 * 64 * 4];
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < cols; x++)
            Array.Copy(raw, ((y * scale) * sw + x * scale) * 4, texture, (y * 64 + x) * 4, 4);
        return texture;
    }

    /// <summary>Kopiert einen 4x12x4-Quader an eine andere Stelle und spiegelt ihn (alte Skins haben nur rechte Gliedmaßen).</summary>
    private static void MirrorLimb(byte[] skin, int su, int sv, int du, int dv)
    {
        void Copy(int sx, int sy, int dx, int dy, int w, int h)
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                Array.Copy(skin, ((sy + y) * 64 + sx + x) * 4, skin, ((dy + y) * 64 + dx + (w - 1 - x)) * 4, 4);
        }
        const int d = 4, w = 4, h = 12;
        Copy(su + d, sv, du + d, dv, w, d);                // oben
        Copy(su + d + w, sv, du + d + w, dv, w, d);        // unten
        Copy(su, sv + d, du + d + w, dv + d, d, h);        // rechte Seite wird zur linken
        Copy(su + d, sv + d, du + d, dv + d, w, h);        // vorne
        Copy(su + d + w, sv + d, du, dv + d, d, h);        // linke Seite wird zur rechten
        Copy(su + 2 * d + w, sv + d, du + 2 * d + w, dv + d, w, h); // hinten
    }
}
