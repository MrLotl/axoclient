using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace AxoClient.UI.Controls;

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
        Background = Brushes.Transparent;
        Cursor = Cursors.SizeWE;
        ToolTip = "Ziehen zum Drehen, Doppelklick dreht zurück nach vorne";

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
                ResetView();
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

    public void ResetView() => TurnTo(DefaultYaw, ResetSeconds);

    public const double FrontYaw = DefaultYaw, BackYaw = 180 + DefaultYaw;

    private double _turnFrom, _turnTo, _pitchFrom, _turnSeconds;
    private TimeSpan _turnStart;
    private bool _turning;
    private const double TurnSeconds = 0.6;
    private const double ResetSeconds = 1.1;

    public void TurnTo(double yaw, double seconds = TurnSeconds)
    {
        _turnFrom = _yaw.Angle;
        _turnSeconds = seconds;
        _turnTo = _turnFrom + ((yaw - _turnFrom) % 360 + 540) % 360 - 180;
        if ((Math.Abs(_turnTo - _turnFrom) < 0.5 && Math.Abs(_pitch.Angle - DefaultPitch) < 0.5) || !IsVisible)
        {
            StopTurn();
            _yaw.Angle = _turnTo;
            _pitch.Angle = DefaultPitch;
            return;
        }
        _pitchFrom = _pitch.Angle;
        _turnStart = TimeSpan.Zero;
        if (!_turning)
            CompositionTarget.Rendering += Turn;
        _turning = true;
    }

    private void StopTurn()
    {
        if (_turning)
            CompositionTarget.Rendering -= Turn;
        _turning = false;
    }

    private void Turn(object? sender, EventArgs e)
    {
        if (_dragging)
        {
            StopTurn();
            return;
        }
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_turnStart == TimeSpan.Zero)
            _turnStart = now;
        var t = Math.Clamp((now - _turnStart).TotalSeconds / _turnSeconds, 0, 1);
        var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        _yaw.Angle = _turnFrom + (_turnTo - _turnFrom) * eased;
        _pitch.Angle = _pitchFrom + (DefaultPitch - _pitchFrom) * eased;
        if (t >= 1)
            StopTurn();
    }

    public void SetSkin(byte[]? png, bool slim, byte[]? capePng = null, bool elytra = false)
    {
        _figure.Children.Clear();
        if (png == null)
            return;

        byte[] skin;
        try
        {
            skin = ReadSkin(png);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Skin für die 3D-Ansicht lesen", ex);
            return;
        }

        var meshes = new Dictionary<uint, MeshGeometry3D>();
        var arm = slim ? 3 : 4;

        void Box(int u, int v, int w, int h, int d, double x, double y, double z, double inflate, bool overlay) =>
            AddBox(skin, meshes, u, v, w, h, d, x, y, z, inflate, overlay);

        Box(0, 0, 8, 8, 8, -4, 24, -4, 0, false);
        Box(16, 16, 8, 12, 4, -4, 12, -2, 0, false);
        Box(40, 16, arm, 12, 4, -4 - arm, 12, -2, 0, false);
        Box(32, 48, arm, 12, 4, 4, 12, -2, 0, false);
        Box(0, 16, 4, 12, 4, -4, 0, -2, 0, false);
        Box(16, 48, 4, 12, 4, 0, 0, -2, 0, false);

        Box(32, 0, 8, 8, 8, -4, 24, -4, 0.5, true);
        Box(16, 32, 8, 12, 4, -4, 12, -2, 0.25, true);
        Box(40, 32, arm, 12, 4, -4 - arm, 12, -2, 0.25, true);
        Box(48, 48, arm, 12, 4, 4, 12, -2, 0.25, true);
        Box(0, 32, 4, 12, 4, -4, 0, -2, 0.25, true);
        Box(0, 48, 4, 12, 4, 0, 0, -2, 0.25, true);

        AddMeshes(_figure, meshes);

        if (capePng != null)
            AddCape(capePng, elytra);
    }

    private void AddCape(byte[] capePng, bool elytra)
    {
        if (elytra)
        {
            AddElytra(capePng);
            return;
        }
        byte[] texture;
        int scale;
        try
        {
            texture = ReadCapeTexture(capePng, out scale);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Umhang für die 3D-Ansicht lesen", ex);
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
                    new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 8)),
                    new TranslateTransform3D(0, 24, -2.85)
                }
            }
        };
        AddMeshes(cape, meshes);
        _figure.Children.Add(cape);
    }

    private void AddElytra(byte[] capePng)
    {
        byte[] texture;
        int scale;
        try
        {
            texture = ReadCapeTexture(capePng, out scale);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Elytra für die 3D-Ansicht lesen", ex);
            return;
        }

        void Wing(double x, bool mirror, double tilt)
        {
            var meshes = new Dictionary<uint, MeshGeometry3D>();
            AddBox(texture, meshes, 22, 0, 10, 20, 2, x, -20, -2, 1, true, scale, mirror);
            var wing = new Model3DGroup
            {
                Transform = new Transform3DGroup
                {
                    Children =
                    {
                        new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 15)),
                        new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), tilt)),
                        new TranslateTransform3D(mirror ? -5 : 5, 24, -2)
                    }
                }
            };
            AddMeshes(wing, meshes);
            _figure.Children.Add(wing);
        }

        Wing(-10, mirror: false, tilt: 15);
        Wing(0, mirror: true, tilt: -15);
    }

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

    private static void AddBox(byte[] skin, Dictionary<uint, MeshGeometry3D> meshes, int u, int v, int w, int h, int d,
                               double x, double y, double z, double inflate, bool overlay, int scale = 1,
                               bool mirror = false)
    {
        double x0 = x - inflate, x1 = x + w + inflate;
        double y0 = y - inflate, y1 = y + h + inflate;
        double z0 = z - inflate, z1 = z + d + inflate;

        void Face(Point3D tl, Point3D tr, Point3D bl, int fu, int fv, int fw, int fh)
        {
            Vector3D right = (tr - tl) / (fw * scale), down = (bl - tl) / (fh * scale);
            for (var py = 0; py < fh * scale; py++)
            for (var px = 0; px < fw * scale; px++)
            {
                var tx = mirror ? fw * scale - 1 - px : px;
                var i = ((fv * scale + py) * 64 * scale + fu * scale + tx) * 4;
                var alpha = skin[i + 3];
                if (overlay && alpha < 128)
                    continue;
                var argb = (uint)(skin[i + 2] << 16 | skin[i + 1] << 8 | skin[i]);
                if (scale > 1)
                    argb &= 0xF8F8F8;
                if (!meshes.TryGetValue(argb, out var mesh))
                    meshes[argb] = mesh = new MeshGeometry3D();

                var p = tl + right * px + down * py;
                var n = mesh.Positions.Count;
                mesh.Positions.Add(p);
                mesh.Positions.Add(p + right);
                mesh.Positions.Add(p + right + down);
                mesh.Positions.Add(p + down);
                foreach (var k in new[] { 0, 3, 2, 0, 2, 1 })
                    mesh.TriangleIndices.Add(n + k);
            }
        }

        Face(new(x0, y1, z1), new(x1, y1, z1), new(x0, y0, z1), u + d, v + d, w, h);
        Face(new(x1, y1, z0), new(x0, y1, z0), new(x1, y0, z0), u + 2 * d + w, v + d, w, h);
        Face(new(x0, y1, z0), new(x0, y1, z1), new(x0, y0, z0), mirror ? u + d + w : u, v + d, d, h);
        Face(new(x1, y1, z1), new(x1, y1, z0), new(x1, y0, z1), mirror ? u : u + d + w, v + d, d, h);
        Face(new(x0, y1, z0), new(x1, y1, z0), new(x0, y1, z1), u + d, v, w, d);
        Face(new(x0, y0, z1), new(x1, y0, z1), new(x0, y0, z0), u + d + w, v, w, d);
    }

    private static byte[] ReadSkin(byte[] png)
    {
        var skin = ReadTexture(png, out var legacy);
        if (legacy)
        {
            MirrorLimb(skin, 0, 16, 16, 48);
            MirrorLimb(skin, 40, 16, 32, 48);
        }
        return skin;
    }

    private static byte[] ReadCapeTexture(byte[] png, out int scale)
    {
        var (raw, sw, sh) = Images.ReadBgra(png);

        scale = Math.Clamp(sw / 64, 1, 8);
        var step = Math.Max(1, sw / (64 * scale));
        int width = 64 * scale, height = 32 * scale;
        var texture = new byte[width * height * 4];
        for (var y = 0; y < height && y * step < sh; y++)
        for (var x = 0; x < width && x * step < sw; x++)
            Array.Copy(raw, ((y * step) * sw + x * step) * 4, texture, (y * width + x) * 4, 4);
        return texture;
    }

    private static byte[] ReadTexture(byte[] png, out bool halfHeight)
    {
        var (raw, sw, sh) = Images.ReadBgra(png);

        var scale = Math.Max(1, sw / 64);
        halfHeight = sh * 2 == sw;
        int rows = Math.Min(64, sh / scale), cols = Math.Min(64, sw / scale);

        var texture = new byte[64 * 64 * 4];
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < cols; x++)
            Array.Copy(raw, ((y * scale) * sw + x * scale) * 4, texture, (y * 64 + x) * 4, 4);
        return texture;
    }

    private static void MirrorLimb(byte[] skin, int su, int sv, int du, int dv)
    {
        void Copy(int sx, int sy, int dx, int dy, int w, int h)
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                Array.Copy(skin, ((sy + y) * 64 + sx + x) * 4, skin, ((dy + y) * 64 + dx + (w - 1 - x)) * 4, 4);
        }
        const int d = 4, w = 4, h = 12;
        Copy(su + d, sv, du + d, dv, w, d);
        Copy(su + d + w, sv, du + d + w, dv, w, d);
        Copy(su, sv + d, du + d + w, dv + d, d, h);
        Copy(su + d, sv + d, du + d, dv + d, w, h);
        Copy(su + d + w, sv + d, du, dv + d, d, h);
        Copy(su + 2 * d + w, sv + d, du + 2 * d + w, dv + d, w, h);
    }
}
