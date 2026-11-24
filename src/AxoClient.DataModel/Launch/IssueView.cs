using AxoClient.Common;

namespace AxoClient.DataModel.Launch
{
    /// <summary>Was die UI im Dialog zeigt.</summary>
    public sealed record IssueView(string Id, Severity Severity, TextRef Message, TextRef? Detail, IReadOnlyList<ActionView> Actions, bool CanSilence);
}
