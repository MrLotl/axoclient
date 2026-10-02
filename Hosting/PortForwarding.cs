using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace AxoClient.Hosting;

public sealed record PortMapping(string ControlUrl, string ServiceType, int Port, string PublicIp, string LanIp);

public sealed record Reachability(string? PublicAddress, string? LanAddress, string? Problem)
{
    public static readonly Reachability Pending = new(null, null, null);
}

public static class PortForwarding
{
    private static readonly string[] ServiceTypes =
    [
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1"
    ];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    public static string? LanIp()
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

    public static async Task<PortMapping> OpenAsync(int port, string description, CancellationToken cancel)
    {
        var lanIp = LanIp() ?? throw new InvalidOperationException("Keine Netzwerkverbindung gefunden.");
        var (controlUrl, serviceType) = await FindGatewayAsync(cancel)
            ?? throw new InvalidOperationException(
                "Kein Router mit UPnP gefunden. Aktiviere in den Router-Einstellungen „Änderungen der Sicherheitseinstellungen über UPnP gestatten“ " +
                "(FritzBox: Internet → Freigaben → Gerät → „Selbstständige Portfreigaben“).");

        await SoapAsync(controlUrl, serviceType, "AddPortMapping", new()
        {
            ["NewRemoteHost"] = "",
            ["NewExternalPort"] = port.ToString(),
            ["NewProtocol"] = "TCP",
            ["NewInternalPort"] = port.ToString(),
            ["NewInternalClient"] = lanIp,
            ["NewEnabled"] = "1",
            ["NewPortMappingDescription"] = description,
            ["NewLeaseDuration"] = "0"
        }, cancel);

        var answer = await SoapAsync(controlUrl, serviceType, "GetExternalIPAddress", new(), cancel);
        var publicIp = answer.Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress")?.Value.Trim() ?? "";
        var mapping = new PortMapping(controlUrl, serviceType, port, publicIp, lanIp);
        if (!IPAddress.TryParse(publicIp, out var address) || IsPrivate(address))
        {
            await CloseAsync(mapping);
            throw new InvalidOperationException(
                "Dein Internetanschluss hat keine eigene öffentliche IPv4-Adresse (z. B. DS-Lite oder CGNAT). " +
                "Freunde im Internet können deinen Server so nicht erreichen, nur Spieler im selben Netzwerk.");
        }
        return mapping;
    }

    public static async Task CloseAsync(PortMapping mapping)
    {
        try
        {
            await SoapAsync(mapping.ControlUrl, mapping.ServiceType, "DeletePortMapping", new()
            {
                ["NewRemoteHost"] = "",
                ["NewExternalPort"] = mapping.Port.ToString(),
                ["NewProtocol"] = "TCP"
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Portfreigabe entfernen", ex);
        }
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length != 4
               || b[0] == 10
               || b[0] == 127
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
               || (b[0] == 169 && b[1] == 254)
               || b[0] == 0;
    }

    private static async Task<(string ControlUrl, string ServiceType)?> FindGatewayAsync(CancellationToken cancel)
    {
        foreach (var location in await DiscoverAsync(cancel))
        {
            try
            {
                var xml = XDocument.Parse(await Http.GetStringAsync(location, cancel));
                var urlBase = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value.Trim();
                var baseUri = new Uri(string.IsNullOrEmpty(urlBase) ? location : urlBase);
                foreach (var serviceType in ServiceTypes)
                {
                    var service = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "service"
                        && e.Elements().Any(c => c.Name.LocalName == "serviceType" && c.Value.Trim() == serviceType));
                    var control = service?.Elements().FirstOrDefault(c => c.Name.LocalName == "controlURL")?.Value.Trim();
                    if (!string.IsNullOrEmpty(control))
                        return (new Uri(baseUri, control).ToString(), serviceType);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
            {
                ErrorReport.Log("UPnP-Gerät abfragen", ex);
            }
        }
        return null;
    }

    private static async Task<List<string>> DiscoverAsync(CancellationToken cancel)
    {
        var locations = new List<string>();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        foreach (var searchTarget in ServiceTypes.Prepend("urn:schemas-upnp-org:device:InternetGatewayDevice:1"))
        {
            var request = Encoding.ASCII.GetBytes(
                "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
                $"ST: {searchTarget}\r\n\r\n");
            await udp.SendAsync(request, request.Length, target);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
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
                    locations.Add(location);
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
        }
        return locations;
    }

    private static async Task<XDocument> SoapAsync(string controlUrl, string serviceType, string action,
        Dictionary<string, string> arguments, CancellationToken cancel)
    {
        var body = new StringBuilder();
        body.Append("<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" ")
            .Append("s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>")
            .Append($"<u:{action} xmlns:u=\"{serviceType}\">");
        foreach (var (name, value) in arguments)
            body.Append($"<{name}>{SecurityElement.Escape(value)}</{name}>");
        body.Append($"</u:{action}></s:Body></s:Envelope>");

        using var request = new HttpRequestMessage(HttpMethod.Post, controlUrl)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "text/xml")
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{serviceType}#{action}\"");
        using var response = await Http.SendAsync(request, cancel);
        var text = await response.Content.ReadAsStringAsync(cancel);
        if (!response.IsSuccessStatusCode)
        {
            string? code = null;
            try
            {
                code = XDocument.Parse(text).Descendants().FirstOrDefault(e => e.Name.LocalName == "errorDescription")?.Value;
            }
            catch (System.Xml.XmlException)
            {
            }
            throw new InvalidOperationException($"Der Router hat die Portfreigabe abgelehnt ({code ?? $"Status {(int)response.StatusCode}"}). " +
                                                "Ist UPnP im Router für diesen PC erlaubt?");
        }
        return XDocument.Parse(text);
    }
}
