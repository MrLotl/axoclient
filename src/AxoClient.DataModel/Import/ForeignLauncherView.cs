using AxoClient.Common;

namespace AxoClient.DataModel.Import
{
    public sealed record ForeignLauncherView(string Id, string Name, bool Installed, IReadOnlyList<ForeignInstanceView> Instances, TextRef? Problem);
}
