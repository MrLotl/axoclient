using AxoClient.Common;

namespace AxoClient.DataModel.Launch
{
    /// <summary>Eine Maßnahme, die der Nutzer im Dialog wählen kann (oder die als Auto-Fix still läuft).</summary>
    public sealed record CheckAction(string Id, TextRef Label, Func<LaunchContext, JobContext, Task> Run, bool Primary = false);
}
