namespace AxoClient.DataModel.Import
{
    /// <summary>Was mitkommen soll. Mods schließen ihre Konfiguration und Daten-Ordner (Minimap usw.) ein.</summary>
    public sealed record ForeignImportParts(bool Worlds = true, bool Mods = true, bool Packs = true, bool Settings = true, bool Screenshots = true);
}
