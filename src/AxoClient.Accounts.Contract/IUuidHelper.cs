namespace AxoClient.Accounts.Contract
{
    public interface IUuidHelper
    {
        /// <summary>Kompakte UUID (32 Zeichen) in die Form mit Bindestrichen bringen.</summary>
        string FormatUuid(string uuid);

        /// <summary>UUID für Adressen beim Dienst: kompakt, nur Hex-Zeichen, sonst Fehler.</summary>
        string RequireUuid(string uuid);
    }
}
