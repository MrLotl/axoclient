namespace AxoClient.DataModel.Launch
{
    /// <summary>Schnellstart in eine Welt oder auf einen Server.</summary>
    public sealed record QuickPlay(string? World = null, string? Server = null);
}
