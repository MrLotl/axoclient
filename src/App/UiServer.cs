using System.Net;
using System.Net.Sockets;
using System.Text;
using AxoClient.Common;

namespace AxoClient.App
{
    /// <summary>
    /// Liefert die eingebettete Oberfläche und <c>/axo/…</c>-Bilder über <c>http://127.0.0.1:&lt;Port&gt;</c>.
    /// Ein eigenes Schema (<c>app://</c>) hat in WebView2 unter Photino keinen echten Ursprung: Module, Schriften und
    /// WebGL-Texturen scheitern dann an CORS. Nur GET/HEAD, nur Loopback, Host-Header wird geprüft (gegen DNS-Rebinding),
    /// und <c>Cross-Origin-Resource-Policy</c> verhindert, dass fremde Seiten Inhalte einbinden.
    /// Die Bridge (Befehle) läuft weiterhin ausschließlich über Photino-Nachrichten, nicht über HTTP.
    /// </summary>
    public sealed class UiServer : IDisposable
    {
        private readonly SchemeHandler _content;
        private readonly IAppLog _appLog;
        private const int MaxHeaderBytes = 16 * 1024;
        private readonly CancellationTokenSource _cts = new();
        private TcpListener? _listener;

        public UiServer(SchemeHandler content, IAppLog appLog)
        {
            _content = content;
            _appLog = appLog;
        }

        public int Port { get; private set; }

        public Uri BaseUri => new($"http://127.0.0.1:{Port}/");

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(() => acceptLoop(_cts.Token));
        }

        private async Task acceptLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }
                _ = Task.Run(() => serve(client, cancellationToken), cancellationToken);
            }
        }

        private async Task serve(TcpClient client, CancellationToken cancellationToken)
        {
            using var _ = client;
            client.NoDelay = true;
            var stream = client.GetStream();
            try
            {
                // Keep-Alive: mehrere Anfragen pro Verbindung, bis der Browser schließt oder 30 s Ruhe ist
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    idle.CancelAfter(TimeSpan.FromSeconds(30));
                    var head = await readHead(stream, idle.Token);
                    if (head == null)
                    {
                        return;
                    }

                    if (!await handle(stream, head, cancellationToken))
                    {
                        return;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                _appLog.Warn("UI-Server: Anfrage fehlgeschlagen", exception);
            }
        }

        /// <summary>Beantwortet eine Anfrage. Gibt false zurück, wenn die Verbindung danach geschlossen werden soll.</summary>
        private async Task<bool> handle(NetworkStream stream, string head, CancellationToken cancellationToken)
        {
            var lines = head.Split("\r\n");
            var parts = lines[0].Split(' ');
            if (parts.Length != 3 || !parts[1].StartsWith('/'))
            {
                await write(stream, 400, "text/plain", [], false, false, cancellationToken);
                return false;
            }
            var method = parts[0];
            var target = parts[1];
            var host = lines.Skip(1).Select(line => line.Split(':', 2))
                .FirstOrDefault(headerParts => headerParts.Length == 2 && headerParts[0].Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))?[1].Trim();
            var keepAlive = !lines.Any(line => line.Equals("Connection: close", StringComparison.OrdinalIgnoreCase));

            if (host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}")
            {
                await write(stream, 403, "text/plain", [], false, false, cancellationToken);
                return false;
            }
            if (method is not ("GET" or "HEAD"))
            {
                await write(stream, 405, "text/plain", [], false, keepAlive, cancellationToken);
                return keepAlive;
            }

            var url = "http://127.0.0.1" + target;
            var isAxo = target.StartsWith("/axo/", StringComparison.Ordinal);
            using var body = isAxo ? _content.Axo(url, out var type) : _content.App(url, out type);
            var bytes = await readAll(body, cancellationToken);
            var status = isAxo && bytes.Length == 0 ? 404 : 200;
            // Gebaute Dateien unter /assets/ tragen einen Hash im Namen und ändern sich nie
            // Panoramas und Bilder mit Versionsangabe (?v=…) bleiben unter derselben Adresse gleich: zwischenspeichern,
            // sonst lädt und dekodiert die Oberfläche sie bei jedem Seitenwechsel neu (sichtbares Nachladen)
            var immutable = status == 200 && ((!isAxo && target.StartsWith("/assets/", StringComparison.Ordinal))
                || (isAxo && (target.StartsWith("/axo/panorama/", StringComparison.Ordinal) || target.Contains("?v=", StringComparison.Ordinal))));
            await write(stream, status, type, method == "HEAD" ? [] : bytes, immutable, keepAlive, cancellationToken, bytes.Length);
            return keepAlive;
        }

        private async Task<byte[]> readAll(Stream? body, CancellationToken cancellationToken)
        {
            if (body == null)
            {
                return [];
            }

            if (body is MemoryStream stream)
            {
                return stream.ToArray();
            }

            using var copy = new MemoryStream();
            await body.CopyToAsync(copy, cancellationToken);
            return copy.ToArray();
        }

        private async Task write(NetworkStream stream, int status, string contentType, byte[] body, bool immutable,
            bool keepAlive, CancellationToken cancellationToken, int? length = null)
        {
            var reason = status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", _ => "Method Not Allowed" };
            var header = new StringBuilder()
                .Append($"HTTP/1.1 {status} {reason}\r\n")
                .Append($"Content-Type: {contentType}\r\n")
                .Append($"Content-Length: {length ?? body.Length}\r\n")
                .Append(immutable ? "Cache-Control: public, max-age=31536000, immutable\r\n" : "Cache-Control: no-cache\r\n")
                .Append("X-Content-Type-Options: nosniff\r\n")
                .Append("Cross-Origin-Resource-Policy: same-origin\r\n")
                .Append("Referrer-Policy: no-referrer\r\n")
                .Append(keepAlive ? "Connection: keep-alive\r\n" : "Connection: close\r\n")
                .Append("\r\n")
                .ToString();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
            if (body.Length > 0)
            {
                await stream.WriteAsync(body, cancellationToken);
            }

            await stream.FlushAsync(cancellationToken);
        }

        /// <summary>Liest Anfragezeile und Header bis zur Leerzeile. null, wenn die Verbindung endet.</summary>
        private async Task<string?> readHead(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new List<byte>(1024);
            var one = new byte[1];
            while (buffer.Count < MaxHeaderBytes)
            {
                var read = await stream.ReadAsync(one, cancellationToken);
                if (read == 0)
                {
                    return null;
                }

                buffer.Add(one[0]);
                var bufferedCount = buffer.Count;
                if (bufferedCount >= 4 && buffer[bufferedCount - 4] == '\r' && buffer[bufferedCount - 3] == '\n' && buffer[bufferedCount - 2] == '\r' && buffer[bufferedCount - 1] == '\n')
                {
                    return Encoding.ASCII.GetString(buffer.ToArray(), 0, bufferedCount - 4);
                }
            }
            return null;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener?.Stop();
            _cts.Dispose();
        }
    }
}
