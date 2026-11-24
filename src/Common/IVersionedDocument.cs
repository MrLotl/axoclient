namespace AxoClient.Common
{
    /// <summary>Jede gespeicherte Datei trägt eine Formatversion für spätere Migrationen.</summary>
    public interface IVersionedDocument
    {
        int FormatVersion { get; set; }
    }
}
