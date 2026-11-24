using System.IO.Pipes;
using System.Text;
using AxoClient.Common;

namespace AxoClient.App
{
    /// <summary>
    /// Nur ein Launcher pro Windows-Benutzer: benannter Mutex. Ein zweiter Start schickt seine Argumente über eine
    /// Named Pipe an den ersten und beendet sich; der erste holt sein Fenster nach vorn.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private readonly IAppLog _appLog;
        private readonly string _name = $"AxoClient-{Environment.UserName}-{Environment.UserDomainName}".Replace('\\', '_');
        private Mutex? _mutex;
        private readonly CancellationTokenSource _cts = new();

        public SingleInstance(IAppLog appLog)
        {
            _appLog = appLog;
        }

        /// <summary>Wird ausgelöst, wenn ein zweiter Start sich meldet (mit seinen Argumenten).</summary>
        public event Action<string[]>? Activated;

        /// <summary>Erster Launcher dieses Benutzers? Sonst bekommt der erste die Argumente und dieser beendet sich.</summary>
        public bool TryAcquire(string[] args)
        {
            var mutex = new Mutex(initiallyOwned: true, $"Local\\{_name}", out var created);
            if (!created)
            {
                mutex.Dispose();
                notifyFirst(args);
                return false;
            }
            _mutex = mutex;
            _ = listen();
            return true;
        }

        private void notifyFirst(string[] args)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.Out);
                pipe.Connect(2000);
                var bytes = Encoding.UTF8.GetBytes(string.Join('\n', args.Prepend("activate")));
                pipe.Write(bytes);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
            }
        }

        private async Task listen()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_cts.Token);
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var text = await reader.ReadToEndAsync(_cts.Token);
                    var lines = text.Split('\n');
                    if (lines.Length > 0 && lines[0] == "activate")
                    {
                        Activated?.Invoke(lines.Skip(1).Take(10).ToArray());
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException ex)
                {
                    _appLog.Warn("Named Pipe", ex);
                    await Task.Delay(500);
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            if (_mutex == null)
            {
                return;
            }
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

    /// <summary>Einfache Windows-Meldungen, bevor das Fenster steht.</summary>
    public class NativeMessage
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        public void Show(string title, string text) => MessageBoxW(IntPtr.Zero, text, title, 0x10);

        /// <summary>Hinweis mit "Wiederholen"/"Abbrechen"; <c>true</c> bei Wiederholen.</summary>
        public bool RetryCancel(string title, string text) => MessageBoxW(IntPtr.Zero, text, title, 0x05 | 0x30) == 4;

        /// <summary>Frage mit "Ja"/"Nein"; <c>true</c> bei Ja.</summary>
        public bool YesNo(string title, string text) => MessageBoxW(IntPtr.Zero, text, title, 0x04 | 0x20) == 6;
    }
}
