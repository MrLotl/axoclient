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

        public async Task<PortMapping> Open(int port, string description, CancellationToken cancellationToken)
        {
            var lanIp = LanIp() ?? throw new AxoException("servers.upnp.noNetwork");
            var (controlUrl, serviceType) = await findGateway(cancellationToken) ?? throw new AxoException("servers.upnp.noRouter");
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
            }, cancellationToken);

            var answer = await soap(controlUrl, serviceType, "GetExternalIPAddress", new(), cancellationToken);
            var publicIp = answer.Descendants().FirstOrDefault(element => element.Name.LocalName == "NewExternalIPAddress")?.Value.Trim() ?? "";
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
            catch (Exception exception) when (exception is HttpRequestException or AxoException or TaskCanceledException)
            {
                _appLog.Warn("Portfreigabe konnte nicht entfernt werden", exception);
            }
        }

        internal bool IsPrivate(IPAddress address)
        {
            var addressBytes = address.GetAddressBytes();
            return addressBytes.Length != 4 || addressBytes[0] == 10 || addressBytes[0] == 127 || (addressBytes[0] == 172 && addressBytes[1] >= 16 && addressBytes[1] <= 31) || (addressBytes[0] == 192 && addressBytes[1] == 168)
                   || (addressBytes[0] == 100 && addressBytes[1] >= 64 && addressBytes[1] <= 127) || (addressBytes[0] == 169 && addressBytes[1] == 254) || addressBytes[0] == 0;
        }

        /// <summary>Nur Router im eigenen Netz ansprechen, nie fremde Adressen aus einer SSDP-Antwort.</summary>
        private bool isLocalUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && IPAddress.TryParse(uri.Host, out var address) && IsPrivate(address);

        private async Task<(string ControlUrl, string ServiceType)?> findGateway(CancellationToken cancellationToken)
        {
            foreach (var location in await discover(cancellationToken))
            {
                if (!isLocalUrl(location))
                {
                    continue;
                }

                try
                {
                    var xml = XDocument.Parse(await _http.GetStringAsync(location, cancellationToken));
                    var urlBase = xml.Descendants().FirstOrDefault(element => element.Name.LocalName == "URLBase")?.Value.Trim();
                    var baseUri = new Uri(string.IsNullOrEmpty(urlBase) ? location : urlBase);
                    foreach (var serviceType in _serviceTypes)
                    {
                        var service = xml.Descendants().FirstOrDefault(element => element.Name.LocalName == "service"
                            && element.Elements().Any(child => child.Name.LocalName == "serviceType" && child.Value.Trim() == serviceType));
                        var control = service?.Elements().FirstOrDefault(element => element.Name.LocalName == "controlURL")?.Value.Trim();
                        if (!string.IsNullOrEmpty(control))
                        {
                            return (new Uri(baseUri, control).ToString(), serviceType);
                        }
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Xml.XmlException or UriFormatException)
                {
                    _appLog.Warn("UPnP-Gerät abfragen", exception);
                }
            }
            return null;
        }

        private async Task<List<string>> discover(CancellationToken cancellationToken)
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

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                while (true)
                {
                    var result = await udp.ReceiveAsync(timeout.Token);
                    var text = Encoding.ASCII.GetString(result.Buffer);
                    var line = text.Split("\r\n").FirstOrDefault(part => part.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase));
                    var location = line?[(line.IndexOf(':') + 1)..].Trim();
                    if (!string.IsNullOrEmpty(location) && !locations.Contains(location))
                    {
                        locations.Add(location);
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            return locations;
        }

        private async Task<XDocument> soap(string controlUrl, string serviceType, string action, Dictionary<string, string> arguments,
            CancellationToken cancellationToken)
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
            using var response = await _http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new AxoException("servers.upnp.refused");
            }

            return XDocument.Parse(text);
        }
    }
}
