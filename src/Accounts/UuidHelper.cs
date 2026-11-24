using AxoClient.Accounts.Contract;
using AxoClient.Common;

namespace AxoClient.Accounts
{
    /// <summary>Spieler-UUIDs in einheitlicher Form.</summary>
    public class UuidHelper : IUuidHelper
    {
        /// <summary>Kompakte UUID (32 Zeichen) in die Form mit Bindestrichen bringen.</summary>
        public string FormatUuid(string uuid)
        {
            var c = uuid.Replace("-", "").ToLowerInvariant();
            if (c.Length == 32)
            {
                return $"{c[..8]}-{c[8..12]}-{c[12..16]}-{c[16..20]}-{c[20..]}";
            }
            return uuid;
        }

        /// <summary>UUID für Adressen beim Dienst: kompakt, nur Hex-Zeichen, sonst Fehler.</summary>
        public string RequireUuid(string uuid)
        {
            var compact = uuid.Replace("-", "").ToLowerInvariant();
            if (compact.Length == 32 && compact.All(Uri.IsHexDigit))
            {
                return compact;
            }
            throw new AxoException("errors.invalidId");
        }
    }
}
