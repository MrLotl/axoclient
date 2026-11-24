namespace AxoClient.DataModel.Launch
{
    /// <summary>Antwort aus dem Dialog: welche Maßnahmen ausgeführt und welche Warnungen stummgeschaltet werden.</summary>
    public sealed record PromptDecision(PromptChoice Choice, IReadOnlyDictionary<string, string>? Actions = null, IReadOnlyCollection<string>? Silence = null);
}
