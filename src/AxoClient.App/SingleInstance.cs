using System.IO.Pipes;
using System.Text;
using AxoClient.Core.Common;

namespace AxoClient.App;

/// <summary>
/// Nur ein Launcher pro Windows-Benutzer: benannter Mutex. Ein zweiter Start schickt seine Argumente über eine
/// Named Pipe an den ersten und beendet sich; der erste holt sein Fenster nach vorn.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string Name = $"AxoClient-{Environment.UserName}-{Environment.UserDomainName}".Replace('\\', '_');
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>Wird ausgelöst, wenn ein zweiter Start sich meldet (mit seinen Argumenten).</summary>
    public event Action<string[]>? Activated;

    public static SingleInstance? TryAcquire(string[] args)
    {
        var mutex = new Mutex(initiallyOwned: true, $"Local\\{Name}", out var created);
        if (!created)
        {
            mutex.Dispose();
            NotifyFirst(args);
            return null;
        }
        var instance = new SingleInstance(mutex);
        _ = instance.ListenAsync();
        return instance;
    }

    private static void NotifyFirst(string[] args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            pipe.Connect(2000);
            var bytes = Encoding.UTF8.GetBytes(string.Join('\n', args.Prepend("activate")));
            pipe.Write(bytes);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }
    }

    private async Task ListenAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var text = await reader.ReadToEndAsync(_cts.Token);
                var lines = text.Split('\n');
                if (lines.Length > 0 && lines[0] == "activate")
                    Activated?.Invoke(lines.Skip(1).Take(10).ToArray());
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                AppLog.Warn("Named Pipe", ex);
                await Task.Delay(500);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        _mutex.Dispose();
    }
}

internal static class NativeMessage
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void Show(string title, string text) => MessageBoxW(IntPtr.Zero, text, title, 0x10);

    /// <summary>Hinweis mit "Wiederholen"/"Abbrechen"; <c>true</c> bei Wiederholen.</summary>
    public static bool RetryCancel(string title, string text) => MessageBoxW(IntPtr.Zero, text, title, 0x05 | 0x30) == 4;

    /// <summary>Frage mit "Ja"/"Nein"; <c>true</c> bei Ja.</summary>
    public static bool YesNo(string title, string text) => MessageBoxW(IntPtr.Zero, text, title, 0x04 | 0x20) == 6;
}
