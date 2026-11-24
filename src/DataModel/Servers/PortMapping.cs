namespace AxoClient.DataModel.Servers
{
    public sealed record PortMapping(string ControlUrl, string ServiceType, int Port, string PublicIp, string LanIp);
}
