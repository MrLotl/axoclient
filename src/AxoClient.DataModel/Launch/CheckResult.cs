using AxoClient.Common;

namespace AxoClient.DataModel.Launch
{
    /// <summary>Ergebnis einer Prüfung. <see cref="Key"/> dient zum Stummschalten (<c>version-mismatch:appleskin@ab12</c>).</summary>
    public sealed record CheckResult(
        string CheckId,
        Severity Severity,
        TextRef Message,
        IReadOnlyList<CheckAction> Actions,
        string? Key = null,
        bool CanSilence = true,
        TextRef? Detail = null)
    {
        public CheckAction? AutoFix => Severity == Severity.AutoFix ? Actions.FirstOrDefault() : null;
    }
}
