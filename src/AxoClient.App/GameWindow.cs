using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AxoClient.App;

/// <summary>
/// Fensterart "Maximiert": Minecraft selbst kennt nur Fenster und Vollbild. Deshalb wartet der Launcher, bis das
/// Spielfenster sichtbar ist, und maximiert es dann wie über die Schaltfläche in der Titelleiste.
/// </summary>
internal static partial class GameWindow
{
    private const int SwMaximize = 3;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(IntPtr hWnd);

    /// <summary>Maximiert das Fenster des Prozesses, sobald es da ist (höchstens drei Minuten lang gesucht).</summary>
    public static void MaximizeWhenShown(int pid) => _ = Task.Run(() => MaximizeAsync(pid));

    private static async Task MaximizeAsync(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var until = DateTime.UtcNow.AddMinutes(3);
            var maximized = 0;
            while (DateTime.UtcNow < until && !process.HasExited)
            {
                await Task.Delay(250).ConfigureAwait(false);
                process.Refresh();
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero || !IsWindowVisible(handle))
                    continue;
                if (!IsZoomed(handle))
                    ShowWindow(handle, SwMaximize);
                // Minecraft setzt die Größe beim Laden manchmal noch einmal zurück: kurz weiter aufpassen
                if (++maximized >= 8)
                    return;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Spiel schon wieder beendet
        }
    }
}
