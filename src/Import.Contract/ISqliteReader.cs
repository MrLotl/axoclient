namespace AxoClient.Import.Contract
{
    public interface ISqliteReader
    {
        /// <summary>
        /// Führt pro Gruppe die erste Abfrage aus, die funktioniert (ältere Launcher-Versionen haben andere Spalten).
        /// Fehlt die Datei oder schlägt alles fehl, ist das Ergebnis der Gruppe leer.
        /// </summary>
        List<List<string?[]>> ReadCopy(string file, params string[][] queryGroups);
    }
}
