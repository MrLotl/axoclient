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

        private async Task acceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener!.AcceptTcpClientAsync(ct);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }
                _ = Task.Run(() => serve(client, ct), ct);
            }
        }

        private async Task serve(TcpClient client, CancellationToken ct)
        {
            using var _ = client;
            client.NoDelay = true;
            var stream = client.GetStream();
            try
            {
                // Keep-Alive: mehrere Anfragen pro Verbindung, bis der Browser schließt oder 30 s Ruhe ist
                while (!ct.IsCancellationRequested)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(TimeSpan.FromSeconds(30));
                    var head = await readHead(stream, idle.Token);
                    if (head == null)
                    {
                        return;
                    }

                    if (!await handle(stream, head, ct))
                    {
                        return;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                _appLog.Warn("UI-Server: Anfrage fehlgeschlagen", ex);
            }
        }

        /// <summary>Beantwortet eine Anfrage. Gibt false zurück, wenn die Verbindung danach geschlossen werden soll.</summary>
        private async Task<bool> handle(NetworkStream stream, string head, CancellationToken ct)
        {
            var lines = head.Split("\r\n");
            var parts = lines[0].Split(' ');
            if (parts.Length != 3 || !parts[1].StartsWith('/'))
            {
                await write(stream, 400, "text/plain", [], false, false, ct);
                return false;
            }
            var method = parts[0];
            var target = parts[1];
            var host = lines.Skip(1).Select(l => l.Split(':', 2))
                .FirstOrDefault(p => p.Length == 2 && p[0].Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))?[1].Trim();
            var keepAlive = !lines.Any(l => l.Equals("Connection: close", StringComparison.OrdinalIgnoreCase));

            if (host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}")
            {
                await write(stream, 403, "text/plain", [], false, false, ct);
                return false;
            }
            if (method is not ("GET" or "HEAD"))
            {
                await write(stream, 405, "text/plain", [], false, keepAlive, ct);
                return keepAlive;
            }

            var url = "http://127.0.0.1" + target;
            var isAxo = target.StartsWith("/axo/", StringComparison.Ordinal);
            using var body = isAxo ? _content.Axo(url, out var type) : _content.App(url, out type);
            var bytes = await readAll(body, ct);
            var status = isAxo && bytes.Length == 0 ? 404 : 200;
            // Gebaute Dateien unter /assets/ tragen einen Hash im Namen und ändern sich nie
            // Panoramas und Bilder mit Versionsangabe (?v=…) bleiben unter derselben Adresse gleich: zwischenspeichern,
            // sonst lädt und dekodiert die Oberfläche sie bei jedem Seitenwechsel neu (sichtbares Nachladen)
            var immutable = status == 200 && ((!isAxo && target.StartsWith("/assets/", StringComparison.Ordinal))
                || (isAxo && (target.StartsWith("/axo/panorama/", StringComparison.Ordinal) || target.Contains("?v=", StringComparison.Ordinal))));
            await write(stream, status, type, method == "HEAD" ? [] : bytes, immutable, keepAlive, ct, bytes.Length);
            return keepAlive;
        }

        private async Task<byte[]> readAll(Stream? body, CancellationToken ct)
        {
            if (body == null)
            {
                return [];
            }

            if (body is MemoryStream ms)
            {
                return ms.ToArray();
            }

            using var copy = new MemoryStream();
            await body.CopyToAsync(copy, ct);
            return copy.ToArray();
        }

        private async Task write(NetworkStream stream, int status, string contentType, byte[] body, bool immutable,
            bool keepAlive, CancellationToken ct, int? length = null)
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
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
            if (body.Length > 0)
            {
                await stream.WriteAsync(body, ct);
            }

            await stream.FlushAsync(ct);
        }

        /// <summary>Liest Anfragezeile und Header bis zur Leerzeile. null, wenn die Verbindung endet.</summary>
        private async Task<string?> readHead(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new List<byte>(1024);
            var one = new byte[1];
            while (buffer.Count < MaxHeaderBytes)
            {
                var read = await stream.ReadAsync(one, ct);
                if (read == 0)
                {
                    return null;
                }

                buffer.Add(one[0]);
                var n = buffer.Count;
                if (n >= 4 && buffer[n - 4] == '\r' && buffer[n - 3] == '\n' && buffer[n - 2] == '\r' && buffer[n - 1] == '\n')
                {
                    return Encoding.ASCII.GetString(buffer.ToArray(), 0, n - 4);
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
