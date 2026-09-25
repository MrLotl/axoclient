using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AxoClient.Platform;

public static class NativeWindows
{
    private const int ShowMaximized = 3;
    private const uint WmSysCommand = 0x0112;
    private const int ScMaximize = 0xF030;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmRoundCorners = 2;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static IntPtr FindMainWindow(int processId)
    {
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)processId || !IsWindowVisible(window))
                return true;
            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            if (title.Length == 0)
                return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static void MaximizeWhenReady(Process process)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < 480 && !process.HasExited; i++)
                {
                    await Task.Delay(250);
                    var window = FindMainWindow(process.Id);
                    if (window == IntPtr.Zero)
                        continue;
                    await Task.Delay(1500);
                    ShowWindow(window, ShowMaximized);
                    PostMessage(window, WmSysCommand, ScMaximize, IntPtr.Zero);
                    return;
                }
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Spielfenster anpassen", ex);
            }
        });
    }

    public static void UseDarkRoundedFrame(IntPtr window)
    {
        var dark = 1;
        DwmSetWindowAttribute(window, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        var round = DwmRoundCorners;
        DwmSetWindowAttribute(window, DwmWindowCornerPreference, ref round, sizeof(int));
    }
}
