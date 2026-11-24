using System.Runtime.InteropServices;

namespace AxoClient.App;

/// <summary>
/// Rahmenloses Fenster: Ziehen über die eigene Titelleiste und Größe ändern an den Rändern per Win32
/// (<c>WM_NCLBUTTONDOWN</c>), damit sich das Fenster wie ein normales Windows-Fenster verhält (Aero Snap usw.).
/// </summary>
internal static partial class WindowChrome
{
    private const int WmNcLButtonDown = 0xA1;
    private const int HtCaption = 2;
    private const int SwRestore = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    /// <summary>
    /// Startgröße und -position: Photino verlangt bei rahmenlosen Fenstern feste Werte (kein OS-Standard).
    /// Logische Größe × Systemskalierung, höchstens 90 % des Arbeitsbereichs, mittig auf dem Hauptbildschirm.
    /// </summary>
    public static (int X, int Y, int Width, int Height) StartBounds(int width, int height)
    {
        var scale = Math.Max(1, GetDpiForSystem()) / 96.0;
        var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1040);
        var w = Math.Min((int)(width * scale), (int)(area.Width * 0.9));
        var h = Math.Min((int)(height * scale), (int)(area.Height * 0.9));
        return (area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmNcCalcSize = 0x0083;
    private const int GwlStyle = -16;
    private const long WsThickFrame = 0x00040000, WsMinimizeBox = 0x00020000, WsMaximizeBox = 0x00010000, WsSysMenu = 0x00080000;
    private const uint SwpFrameChanged = 0x0020, SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoOwnerZOrder = 0x0200, SwpNoActivate = 0x0010;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    // Muss leben, solange das Fenster existiert (sonst sammelt der GC den Delegaten ein)
    private static SubclassProc? _subclass;

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(IntPtr hWnd, IntPtr proc, UIntPtr id, UIntPtr data);

    [LibraryImport("comctl32.dll")]
    private static partial IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>
    /// Rahmenloses Fenster, das sich wie ein normales Windows-Fenster verhält:
    /// <list type="bullet">
    /// <item>Rahmen- und Maximieren-Stil (WS_THICKFRAME, WS_MAXIMIZEBOX …), sonst greift Aero Snap nicht
    /// (an den oberen Rand ziehen = maximieren, an die Seite = halbe Breite).</item>
    /// <item><c>WM_NCCALCSIZE</c> gibt den ganzen Bereich der Oberfläche, damit der Systemrahmen nicht sichtbar wird.</item>
    /// <item>Rahmenlose Fenster maximiert Windows über den ganzen Bildschirm – die Taskleiste verschwindet.
    /// Bei <c>WM_GETMINMAXINFO</c> wird die maximierte Größe auf den Arbeitsbereich des Monitors begrenzt.
    /// Das greift für jeden Weg: Button, Doppelklick auf die Titelleiste und Aero Snap.</item>
    /// </list>
    /// </summary>
    public static void KeepTaskbarVisible(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _subclass != null)
            return;
        _subclass = (h, msg, wParam, lParam, _, _) =>
        {
            // Kein sichtbarer Systemrahmen: die Oberfläche füllt das ganze Fenster
            if (msg == WmNcCalcSize && wParam != IntPtr.Zero)
                return IntPtr.Zero;
            var result = DefSubclassProc(h, msg, wParam, lParam);
            if (msg != WmGetMinMaxInfo || lParam == IntPtr.Zero)
                return result;
            var monitor = MonitorFromWindow(h, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
                return result;
            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            mmi.MaxPosition = new Point { X = info.Work.Left - info.Monitor.Left, Y = info.Work.Top - info.Monitor.Top };
            mmi.MaxSize = new Point { X = info.Work.Right - info.Work.Left, Y = info.Work.Bottom - info.Work.Top };
            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero;
        };
        SetWindowSubclass(hwnd, Marshal.GetFunctionPointerForDelegate(_subclass), 1, 0);

        // Stile für Aero Snap ergänzen und Windows den Rahmen neu berechnen lassen
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        var wanted = style | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu;
        if (wanted != style)
        {
            SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(wanted));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoOwnerZOrder | SwpNoActivate);
        }
    }

    public static void Drag(IntPtr hwnd) => NonClient(hwnd, HtCaption);

    /// <summary>Kante: left, right, top, bottom, topleft, topright, bottomleft, bottomright.</summary>
    public static void Resize(IntPtr hwnd, string edge)
    {
        var hit = edge switch
        {
            "left" => 10,
            "right" => 11,
            "top" => 12,
            "topleft" => 13,
            "topright" => 14,
            "bottom" => 15,
            "bottomleft" => 16,
            "bottomright" => 17,
            _ => 0
        };
        if (hit != 0)
            NonClient(hwnd, hit);
    }

    private static void NonClient(IntPtr hwnd, int hit)
    {
        if (hwnd == IntPtr.Zero)
            return;
        ReleaseCapture();
        SendMessage(hwnd, WmNcLButtonDown, hit, IntPtr.Zero);
    }

    public static void BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        if (IsIconic(hwnd) || !IsWindowVisible(hwnd))
            ShowWindow(hwnd, SwRestore);
        SetForegroundWindow(hwnd);
    }

    public static void Hide(IntPtr hwnd) => ShowWindow(hwnd, 0);

    public static bool IsHiddenOrMinimized(IntPtr hwnd) => hwnd != IntPtr.Zero && (IsIconic(hwnd) || !IsWindowVisible(hwnd));
}
