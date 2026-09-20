using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace McLauncher;

/// <summary>
/// Minecraft kennt kein Startargument für "maximiert". Deshalb wartet der Launcher, bis das Spielfenster da ist,
/// und maximiert es selbst.
/// </summary>
public static class GameWindow
{
    private const int ShowMaximized = 3;
    private const uint WmSysCommand = 0x0112;
    private const int ScMaximize = 0xF030;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>Maximiert das Spielfenster, sobald es erscheint (höchstens zwei Minuten lang warten).</summary>
    public static void MaximizeWhenReady(Process process)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var id = (uint)process.Id;
                for (var i = 0; i < 480 && !process.HasExited; i++)
                {
                    await Task.Delay(250);
                    var window = FindWindow(id);
                    if (window == IntPtr.Zero)
                        continue;
                    await Task.Delay(1500); // erst das Fenster fertig aufbauen lassen
                    ShowWindow(window, ShowMaximized);
                    PostMessage(window, WmSysCommand, ScMaximize, IntPtr.Zero); // falls das Spiel es zurücksetzt
                    return;
                }
            }
            catch
            {
                // Prozess weg oder Fenster nicht erreichbar: dann bleibt es in normaler Größe
            }
        });
    }

    /// <summary>Das sichtbare Fenster des Prozesses, mit einem Titel (er heißt je nach Client "Minecraft ..." oder "AxoClient* ...").</summary>
    private static IntPtr FindWindow(uint processId)
    {
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != processId || !IsWindowVisible(window))
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
}
