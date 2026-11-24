using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml.Linq;
using AxoClient.Common;
using AxoClient.DataModel.Servers;
using AxoClient.Servers.Contract;

namespace AxoClient.Servers
{
    /// <summary>
    /// Portfreigabe per UPnP für "Für Freunde im Internet öffnen". Standardmäßig aus, beim Stoppen wird der Port
    /// wieder geschlossen.
    /// </summary>
    public sealed class PortForwarding : IPortForwarding
    {
        private readonly ITextHelper _textHelper;
        private readonly IAppLog _appLog;
        private readonly string[] _serviceTypes =
        [
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:1"
        ];

        // Router sprechen nur HTTP im lokalen Netz; deshalb ein eigener Client ohne HTTPS-Zwang
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };

        public PortForwarding(ITextHelper textHelper, IAppLog appLog)
        {
            _textHelper = textHelper;
            _appLog = appLog;
        }

        public string? LanIp()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 53);
                return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
            }
            catch (SocketException)
            {
                return null;
            }
        }

        public async Task<PortMapping> Open(int port, string description, CancellationToken ct)
        {
            var lanIp = LanIp() ?? throw new AxoException("servers.upnp.noNetwork");
            var (controlUrl, serviceType) = await findGateway(ct) ?? throw new AxoException("servers.upnp.noRouter");
            if (!isLocalUrl(controlUrl))
            {
                throw new AxoException("servers.upnp.noRouter");
            }

            await soap(controlUrl, serviceType, "AddPortMapping", new()
            {
                ["NewRemoteHost"] = "",
                ["NewExternalPort"] = port.ToString(),
                ["NewProtocol"] = "TCP",
                ["NewInternalPort"] = port.ToString(),
                ["NewInternalClient"] = lanIp,
                ["NewEnabled"] = "1",
                ["NewPortMappingDescription"] = _textHelper.Clean(description, 60),
                ["NewLeaseDuration"] = "0"
            }, ct);

            var answer = await soap(controlUrl, serviceType, "GetExternalIPAddress", new(), ct);
            var publicIp = answer.Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress")?.Value.Trim() ?? "";
            var mapping = new PortMapping(controlUrl, serviceType, port, publicIp, lanIp);
            if (!IPAddress.TryParse(publicIp, out var address) || IsPrivate(address))
            {
                await Close(mapping);
                throw new AxoException("servers.upnp.noPublicIp");
            }
            return mapping;
        }

        public async Task Close(PortMapping mapping)
        {
            try
            {
                await soap(mapping.ControlUrl, mapping.ServiceType, "DeletePortMapping", new()
                {
                    ["NewRemoteHost"] = "",
                    ["NewExternalPort"] = mapping.Port.ToString(),
                    ["NewProtocol"] = "TCP"
                }, CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or AxoException or TaskCanceledException)
            {
                _appLog.Warn("Portfreigabe konnte nicht entfernt werden", ex);
            }
        }

        internal bool IsPrivate(IPAddress address)
        {
            var b = address.GetAddressBytes();
            return b.Length != 4 || b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || (b[0] == 169 && b[1] == 254) || b[0] == 0;
        }

        /// <summary>Nur Router im eigenen Netz ansprechen, nie fremde Adressen aus einer SSDP-Antwort.</summary>
        private bool isLocalUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && IPAddress.TryParse(uri.Host, out var ip) && IsPrivate(ip);

        private async Task<(string ControlUrl, string ServiceType)?> findGateway(CancellationToken ct)
        {
            foreach (var location in await discover(ct))
            {
                if (!isLocalUrl(location))
                {
                    continue;
                }

                try
                {
                    var xml = XDocument.Parse(await _http.GetStringAsync(location, ct));
                    var urlBase = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value.Trim();
                    var baseUri = new Uri(string.IsNullOrEmpty(urlBase) ? location : urlBase);
                    foreach (var serviceType in _serviceTypes)
                    {
                        var service = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "service"
                            && e.Elements().Any(c => c.Name.LocalName == "serviceType" && c.Value.Trim() == serviceType));
                        var control = service?.Elements().FirstOrDefault(c => c.Name.LocalName == "controlURL")?.Value.Trim();
                        if (!string.IsNullOrEmpty(control))
                        {
                            return (new Uri(baseUri, control).ToString(), serviceType);
                        }
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException or UriFormatException)
                {
                    _appLog.Warn("UPnP-Gerät abfragen", ex);
                }
            }
            return null;
        }

        private async Task<List<string>> discover(CancellationToken ct)
        {
            var locations = new List<string>();
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            foreach (var searchTarget in _serviceTypes.Prepend("urn:schemas-upnp-org:device:InternetGatewayDevice:1"))
            {
                var request = Encoding.ASCII.GetBytes(
                    "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" + $"ST: {searchTarget}\r\n\r\n");
                await udp.SendAsync(request, request.Length, target);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                while (true)
                {
                    var result = await udp.ReceiveAsync(timeout.Token);
                    var text = Encoding.ASCII.GetString(result.Buffer);
                    var line = text.Split("\r\n").FirstOrDefault(l => l.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase));
                    var location = line?[(line.IndexOf(':') + 1)..].Trim();
                    if (!string.IsNullOrEmpty(location) && !locations.Contains(location))
                    {
                        locations.Add(location);
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }
            return locations;
        }

        private async Task<XDocument> soap(string controlUrl, string serviceType, string action, Dictionary<string, string> arguments,
            CancellationToken ct)
        {
            var body = new StringBuilder();
            body.Append("<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" ")
                .Append("s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>")
                .Append($"<u:{action} xmlns:u=\"{serviceType}\">");
            foreach (var (name, value) in arguments)
            {
                body.Append($"<{name}>{SecurityElement.Escape(value)}</{name}>");
            }

            body.Append($"</u:{action}></s:Body></s:Envelope>");

            using var request = new HttpRequestMessage(HttpMethod.Post, controlUrl)
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "text/xml")
            };
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{serviceType}#{action}\"");
            using var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new AxoException("servers.upnp.refused");
            }

            return XDocument.Parse(text);
        }
    }
}
