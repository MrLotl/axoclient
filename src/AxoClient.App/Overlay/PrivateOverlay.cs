using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AxoClient.Common;
using AxoClient.Instances.Contract;
using AxoClient.Integrations.Contract;
using AxoClient.Launch.Contract;

namespace AxoClient.App.Overlay
{
    /// <summary>
    /// Das private Overlay: ein durchsichtiges Fenster genau über dem Minecraft-Fenster, durch das Klicks hindurchgehen und
    /// das Windows aus jeder Bildschirmaufnahme heraushält (Discord, OBS, Screenshots). Darin zeichnet der Launcher die
    /// Widgets, die in der Mod auf "Privat" stehen – mit der Minecraft-Schrift aus der Jar der Instanz, also genauso wie
    /// im Spiel.
    /// <para>
    /// Läuft auf einem eigenen Thread mit eigener Nachrichtenschleife. Sichtbar nur, solange Minecraft das aktive Fenster
    /// ist (sonst stünde es über anderen Programmen). Im exklusiven Vollbild kann Windows nichts darüber legen.
    /// </para>
    /// </summary>
    public sealed class PrivateOverlay : IDisposable
    {
        private readonly IAppLog _appLog;
        private readonly IAxoModBridge _bridge;
        private readonly IGameLauncher _games;
        private readonly IInstanceManager _instances;
        private readonly AppPaths _paths;
        private readonly McFontLoader _fontLoader;
        private readonly Dictionary<string, McFont?> _fonts = new();
        private readonly object _lock = new();
        private OverlayWindow? _window;
        private Thread? _thread;

        /// <summary>Neuestes Bild: Instanz und Befehle; null = nichts zu zeigen.</summary>
        private (string Instance, JsonElement Frame, DateTime At)? _latest;

        /// <summary>Ob Windows Fenster aus Aufnahmen heraushalten kann (ab Windows 10 2004).</summary>
        public bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

        public PrivateOverlay(IAppLog appLog, IAxoModBridge bridge, IGameLauncher games, IInstanceManager instances, AppPaths paths,
            McFontLoader fontLoader, OverlayRenderer renderer)
        {
            _appLog = appLog;
            _bridge = bridge;
            _games = games;
            _instances = instances;
            _paths = paths;
            _fontLoader = fontLoader;
            Renderer = renderer;
        }

        /// <summary>Zeichnet die Befehle der Mod in ein Bild.</summary>
        internal OverlayRenderer Renderer { get; }

        /// <summary>Ab jetzt auf Bilder der Mod hören (nur, wenn <see cref="Supported"/>).</summary>
        public void Start()
        {
            _bridge.PrivateOverlay = true;
            _bridge.MessageReceived += onMessage;
            _games.GameExited += instance =>
            {
                lock (_lock)
                {
                    if (_latest?.Instance == instance)
                    {
                        _latest = null;
                    }
                }
            };
        }

        private void onMessage(string instance, string type, JsonElement data)
        {
            if (type != "hud" || data.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var empty = !data.TryGetProperty("cmds", out var cmds) || cmds.ValueKind != JsonValueKind.Array || cmds.GetArrayLength() == 0;
            lock (_lock)
            {
                _latest = empty ? null : (instance, data, DateTime.UtcNow);
                if (empty || _thread != null)
                {
                    return;
                }

                _thread = new Thread(runWindow) { IsBackground = true, Name = "AxoClient privates Overlay" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }

        private void runWindow()
        {
            try
            {
                System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
                _window = new OverlayWindow(this, _appLog);
                System.Windows.Forms.Application.Run(_window);
            }
            catch (Exception ex)
            {
                _appLog.Error("Privates Overlay beendet", ex);
            }
        }

        /// <summary>
        /// Für das Fenster: aktuelles Bild (mit Zeitstempel), die Minecraft-Schrift und das Spielfenster. Null, solange es
        /// nichts zu zeigen gibt – auch, solange die Schrift noch lädt.
        /// </summary>
        internal (JsonElement Frame, DateTime At, McFont? Font, IntPtr Game)? Current()
        {
            (string Instance, JsonElement Frame, DateTime At)? latest;
            lock (_lock)
            {
                latest = _latest;
            }
            // Kommt eine Sekunde nichts mehr (Spiel hängt, Verbindung weg), nichts mehr zeigen
            if (latest is not { } l || DateTime.UtcNow - l.At > TimeSpan.FromSeconds(1))
            {
                return null;
            }

            var game = _games.Running.FirstOrDefault(g => g.InstanceId == l.Instance);
            if (game == null)
            {
                return null;
            }

            lock (_fonts)
            {
                if (!_fonts.TryGetValue(l.Instance, out var font))
                {
                    _fonts[l.Instance] = null;
                    _loading.Add(l.Instance);
                    _ = loadFont(l.Instance);
                    return null;
                }
                if (_loading.Contains(l.Instance))
                {
                    return null;
                }

                return (l.Frame, l.At, font, gameWindow(game.Pid));
            }
        }

        private readonly HashSet<string> _loading = new();

        /// <summary>Minecraft-Schrift aus der Jar der Instanz (einmal je Instanz, im Hintergrund).</summary>
        private async Task loadFont(string instance)
        {
            McFont? font = null;
            try
            {
                var version = (await _instances.Get(instance)).Minecraft.Version;
                var jar = Path.Combine(_paths.Versions, version, version + ".jar");
                font = await Task.Run(() => File.Exists(jar) ? _fontLoader.Load(jar) : null);
                if (font == null)
                {
                    _appLog.Warn("Privates Overlay: Minecraft-Schrift nicht gefunden in " + jar);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _appLog.Warn("Privates Overlay: Minecraft-Schrift nicht lesbar", ex);
            }
            lock (_fonts)
            {
                _fonts[instance] = font;
                _loading.Remove(instance);
            }
        }

        private readonly Dictionary<int, IntPtr> _windows = new();

        private IntPtr gameWindow(int pid)
        {
            if (_windows.TryGetValue(pid, out var known) && Native.IsWindow(known))
            {
                return known;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                process.Refresh();
                var handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    _windows[pid] = handle;
                }

                return handle;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            _bridge.MessageReceived -= onMessage;
            var window = _window;
            if (window != null && window.IsHandleCreated)
            {
                window.BeginInvoke(window.Close);
            }
        }

        /// <summary>Das Fenster selbst: durchsichtig, ohne Rahmen, nicht anklickbar, aus Aufnahmen ausgenommen.</summary>
        private sealed class OverlayWindow : System.Windows.Forms.Form
        {
            private readonly PrivateOverlay _owner;
            private readonly IAppLog _appLog;
            private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
            private bool _visible;

            public OverlayWindow(PrivateOverlay owner, IAppLog appLog)
            {
                _owner = owner;
                _appLog = appLog;
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                Location = new System.Drawing.Point(-32000, -32000);
                Size = new System.Drawing.Size(1, 1);
                Text = "AxoClient Overlay";
                _timer.Tick += (_, _) => tick();
                _timer.Start();
            }

            protected override bool ShowWithoutActivation => true;

            protected override System.Windows.Forms.CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= Native.WsExLayered | Native.WsExTransparent | Native.WsExToolWindow | Native.WsExNoActivate | Native.WsExTopMost;
                    return cp;
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // Aus Aufnahmen heraushalten; geht das nicht, lieber gar nichts zeigen
                if (!Native.SetWindowDisplayAffinity(Handle, Native.WdaExcludeFromCapture))
                {
                    _appLog.Warn("Privates Overlay: Windows kann das Fenster nicht aus Aufnahmen heraushalten – es bleibt leer");
                    _timer.Stop();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _timer.Dispose();
                }

                base.Dispose(disposing);
            }

            /// <summary>Was zuletzt gezeigt wurde: Bild-Zeitstempel und Lage des Spielfensters (sonst nichts neu zeichnen).</summary>
            private (DateTime At, int X, int Y, int W, int H) _shown;

            private void tick()
            {
                var current = _owner.Current();
                if (current is not { } c || c.Game == IntPtr.Zero || Native.IsIconic(c.Game) || !gameInFront(c.Game)
                    || !Native.GetClientRect(c.Game, out var client) || client.Right <= 0 || client.Bottom <= 0)
                {
                    hide();
                    return;
                }
                var origin = new Native.Point();
                Native.ClientToScreen(c.Game, ref origin);
                var state = (c.At, origin.X, origin.Y, client.Right, client.Bottom);
                if (_visible && state == _shown)
                {
                    return;
                }

                _shown = state;
                var image = _owner.Renderer.Render(c.Frame, client.Right, client.Bottom, c.Font);
                if (image == null)
                {
                    hide();
                    return;
                }
                present(image.Pixels, origin.X + image.X, origin.Y + image.Y, image.Width, image.Height);
                if (!_visible)
                {
                    Native.ShowWindow(Handle, Native.SwShowNoActivate);
                    _visible = true;
                }
            }

            /// <summary>Nur über Minecraft: ist ein anderes Programm vorn, verschwindet das Overlay.</summary>
            private bool gameInFront(IntPtr game)
            {
                var front = Native.GetForegroundWindow();
                return front == game || front == Handle;
            }

            private new void hide()
            {
                if (!_visible)
                {
                    return;
                }

                Native.ShowWindow(Handle, Native.SwHide);
                _visible = false;
                _shown = default;
            }

            /// <summary>Bild mit Deckkraft je Pixel ins Fenster (UpdateLayeredWindow).</summary>
            private void present(uint[] pixels, int x, int y, int width, int height)
            {
                var screen = Native.GetDC(IntPtr.Zero);
                var memory = Native.CreateCompatibleDC(screen);
                // Kopf der Bitmap (BITMAPINFOHEADER, 40 Byte); negative Höhe = Zeilen von oben nach unten
                var info = new Native.BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
                var bitmap = Native.CreateDIBSection(memory, ref info, 0, out var bits, IntPtr.Zero, 0);
                var old = IntPtr.Zero;
                try
                {
                    if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                    {
                        return;
                    }

                    Marshal.Copy((int[])(object)pixels, 0, bits, pixels.Length);
                    old = Native.SelectObject(memory, bitmap);
                    var position = new Native.Point { X = x, Y = y };
                    var size = new Native.Size { Width = width, Height = height };
                    var source = new Native.Point();
                    var blend = new Native.BlendFunction { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                    Native.UpdateLayeredWindow(Handle, screen, ref position, ref size, memory, ref source, 0, ref blend, Native.UlwAlpha);
                }
                finally
                {
                    if (old != IntPtr.Zero)
                    {
                        Native.SelectObject(memory, old);
                    }

                    if (bitmap != IntPtr.Zero)
                    {
                        Native.DeleteObject(bitmap);
                    }

                    Native.DeleteDC(memory);
                    Native.ReleaseDC(IntPtr.Zero, screen);
                }
            }
        }

        private class Native
        {
            public const int WsExLayered = 0x00080000;
            public const int WsExTransparent = 0x00000020;
            public const int WsExToolWindow = 0x00000080;
            public const int WsExNoActivate = 0x08000000;
            public const int WsExTopMost = 0x00000008;
            public const uint WdaExcludeFromCapture = 0x11;
            public const int SwHide = 0;
            public const int SwShowNoActivate = 4;
            public const int UlwAlpha = 2;

            [StructLayout(LayoutKind.Sequential)]
            public struct Point
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Size
            {
                public int Width;
                public int Height;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct BlendFunction
            {
                public byte BlendOp;
                public byte BlendFlags;
                public byte SourceConstantAlpha;
                public byte AlphaFormat;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct BitmapInfo
            {
                public int Size;
                public int Width;
                public int Height;
                public short Planes;
                public short BitCount;
                public int Compression;
                public int SizeImage;
                public int XPelsPerMeter;
                public int YPelsPerMeter;
                public int ClrUsed;
                public int ClrImportant;
                public int Colors;
            }

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsIconic(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hWnd, int cmd);

            [DllImport("user32.dll")]
            public static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

            [DllImport("gdi32.dll")]
            public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteDC(IntPtr hdc);

            [DllImport("gdi32.dll")]
            public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteObject(IntPtr obj);

            [DllImport("gdi32.dll")]
            public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref Point dst, ref Size size, IntPtr hdcSrc, ref Point src,
                int key, ref BlendFunction blend, int flags);
        }
    }
}
