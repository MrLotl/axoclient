using AxoClient.Common;

namespace AxoClient.DataModel.Diagnostics
{
    /// <summary>Eine Zeile im Diagnose-Ergebnis, jeweils mit Lösung.</summary>
    public sealed record DiagnosticItem(string Area, DiagnosticStatus Status, TextRef Title, TextRef? Detail = null, TextRef? Solution = null);
}
