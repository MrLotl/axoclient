using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AxoClient.UI.Controls;

public sealed class PrivacyOverlay : Window
{
    private const uint ExcludeFromCapture = 0x00000011;

    private const uint MonitorOnly = 0x00000001;

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr window, ref Point32 point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X, Y;
    }

    private readonly PrivacyLink _link;
    private readonly PrivacyCanvas _canvas = new();
    private readonly DispatcherTimer _timer;
    private readonly Process _game;
    private IntPtr _gameWindow;

    public bool Hidden { get; private set; }

    public string? Problem { get; private set; }

    public PrivacyOverlay(PrivacyLink link, Process game, string? fontJar = null)
    {
        _canvas.McFont = MinecraftFont.Load(fontJar);
        _link = link;
        _game = game;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        ShowActivated = false;
        Left = 0;
        Top = 0;
        Width = 1;
        Height = 1;
        Content = _canvas;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Follow();

        SourceInitialized += (_, _) => Prepare();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _link.FrameReceived -= OnFrame;
        };
        _link.FrameReceived += OnFrame;
    }

    private void Prepare()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExNoActivate | WsExToolWindow);

        if (SetWindowDisplayAffinity(handle, ExcludeFromCapture))
        {
            Hidden = true;
        }
        else if (SetWindowDisplayAffinity(handle, MonitorOnly))
        {
            Hidden = true;
            Problem = "Dein Windows kennt das vollständige Ausblenden noch nicht (dafür braucht es Windows 10 " +
                      "Version 2004 oder neuer). In Aufnahmen bleibt an dieser Stelle eine schwarze Fläche.";
        }
        else
        {
            Hidden = false;
            Problem = "Windows hat das Ausblenden aus Aufnahmen abgelehnt (" +
                      new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message +
                      "). Die Anzeigen wären in einer Aufnahme zu sehen - deshalb bleiben sie aus.";
        }

        _timer.Start();
        Follow();
    }

    private void OnFrame(PrivacyFrame frame)
    {
        if (frame.Pid != _game.Id)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _canvas.Show(frame);
            Follow();
        });
    }

    private void Follow()
    {
        if (!Hidden)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        if (_gameWindow == IntPtr.Zero || !NativeWindows.IsWindowVisible(_gameWindow))
            _gameWindow = FindGameWindow();

        var frame = _link.FrameOf(_game.Id);
        var fresh = frame != null && DateTime.UtcNow - frame.ReceivedAt < TimeSpan.FromSeconds(2);
        var visible = fresh && _gameWindow != IntPtr.Zero && !NativeWindows.IsIconic(_gameWindow)
                      && NativeWindows.GetForegroundWindow() == _gameWindow;

        if (!visible)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        if (!GetClientRect(_gameWindow, out var client))
            return;
        var corner = new Point32 { X = client.Left, Y = client.Top };
        if (!ClientToScreen(_gameWindow, ref corner))
            return;

        var source = PresentationSource.FromVisual(this);
        var toWpf = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = toWpf.Transform(new Point(corner.X, corner.Y));
        var size = toWpf.Transform(new Point(client.Right - client.Left, client.Bottom - client.Top));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(1, size.X);
        Height = Math.Max(1, size.Y);
        _canvas.SetPixelSize(client.Right - client.Left, client.Bottom - client.Top, size.X, size.Y);
        Visibility = Visibility.Visible;
    }

    private IntPtr FindGameWindow()
    {
        try
        {
            return _game.HasExited ? IntPtr.Zero : NativeWindows.FindMainWindow(_game.Id);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Spielfenster für das private Overlay suchen", ex);
            return IntPtr.Zero;
        }
    }
}

public sealed class PrivacyCanvas : FrameworkElement
{
    private const double LineHeight = 8;

    private static readonly Typeface Font = new(new FontFamily("Consolas"), FontStyles.Normal,
        FontWeights.Normal, FontStretches.Normal);

    private PrivacyFrame? _frame;

    public MinecraftFont? McFont { get; set; }

    public PrivacyCanvas()
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    private double _pixelWidth = 1, _wpfWidth = 1;

    public void Show(PrivacyFrame frame)
    {
        _frame = frame;
        InvalidateVisual();
    }

    public void SetPixelSize(int pixelWidth, int pixelHeight, double wpfWidth, double wpfHeight)
    {
        _pixelWidth = Math.Max(1, pixelWidth);
        _wpfWidth = Math.Max(1, wpfWidth);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        if (_frame is not { } frame)
            return;

        var unit = frame.GuiWidth > 0 ? _wpfWidth / frame.GuiWidth : 0;
        if (unit <= 0 || double.IsNaN(unit))
            return;

        foreach (var command in frame.Cmds)
        {
            var color = ToColor(command.C);
            if (color.A == 0)
                continue;
            var scale = command.S <= 0 ? 1 : command.S;

            if (command.T == "r")
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                var rect = new Rect(command.X * unit, command.Y * unit,
                    Math.Max(0, command.W * scale * unit), Math.Max(0, command.H * scale * unit));
                var radius = command.R * scale * unit;
                if (radius > 0)
                    context.DrawRoundedRectangle(brush, null, rect, radius, radius);
                else
                    context.DrawRectangle(brush, null, rect);
                continue;
            }

            if (command.V.Length == 0)
                continue;
            DrawText(context, command, color, scale, unit);
        }
    }

    private void DrawText(DrawingContext context, PrivacyDraw command, Color color, double scale, double unit)
    {
        if (McFont is { } font && font.CanDraw(command.V))
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            SolidColorBrush? shadowBrush = null;
            if (command.Sh)
            {
                shadowBrush = new SolidColorBrush(Shadow(color));
                shadowBrush.Freeze();
            }
            context.PushTransform(new MatrixTransform(scale * unit, 0, 0, scale * unit, command.X * unit,
                command.Y * unit));
            font.Draw(context, command.V, brush, shadowBrush);
            context.Pop();
            return;
        }

        var height = LineHeight * scale * unit;
        var text = new FormattedText(command.V, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font,
            height * 1.32, new SolidColorBrush(color), 96)
        {
            TextAlignment = TextAlignment.Left
        };

        var wanted = command.W * scale * unit;
        var stretch = wanted > 0 && text.WidthIncludingTrailingWhitespace > 0
            ? wanted / text.WidthIncludingTrailingWhitespace
            : 1;

        var x = command.X * unit;
        var y = command.Y * unit;
        context.PushTransform(new TranslateTransform(x, y));
        context.PushTransform(new ScaleTransform(stretch, 1));
        if (command.Sh)
        {
            var shadow = new FormattedText(command.V, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font,
                height * 1.32, new SolidColorBrush(Shadow(color)), 96);
            context.DrawText(shadow, new Point(scale * unit, scale * unit));
        }
        context.DrawText(text, new Point(0, 0));
        context.Pop();
        context.Pop();
    }

    private static Color Shadow(Color color) =>
        Color.FromArgb(color.A, (byte)(color.R / 4), (byte)(color.G / 4), (byte)(color.B / 4));

    private static Color ToColor(long argb) => Color.FromArgb(
        (byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
}
