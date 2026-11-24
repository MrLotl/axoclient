namespace AxoClient.DataModel.Settings
{
    /// <summary>Vom Nutzer angelegter Verlauf, auswählbar wie eine Vorlage.</summary>
    public sealed record CustomGradient(string Id, string From, string To, int Angle);
}
